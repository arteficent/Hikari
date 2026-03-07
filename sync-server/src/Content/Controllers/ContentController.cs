using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SyncServer.Content.Contracts;
using SyncServer.Content.Dtos;
using SyncServer.Content.Models;
using SyncServer.Content.Registries;
using SyncServer.Content.Repositories;
using SyncServer.Configuration;
using System.Globalization;

namespace SyncServer.Content.Controllers;

/// <summary>
/// Generic content controller. All content-type operations go through this controller.
/// Routes: /content/{contentType}/upload-init, /content/{contentType}/upload-complete, /content/{contentType}/items, /content/{contentType}/download, etc.
/// The {contentType} segment is resolved to the appropriate IContentPlugin.
/// </summary>
[ApiController]
[Route("content/{contentType}")]
public class ContentController : ControllerBase
{
    private const int DefaultUrlExpiryMinutes = 15;
    private const int MaxUrlExpiryMinutes = 60;

    private readonly ILogger<ContentController> _logger;
    private readonly IContentPluginRegistry _pluginRegistry;
    private readonly IContentRepository _contentRepository;
    private readonly IAmazonS3 _s3Client;
    private readonly AmazonWebServicesConstants _awsConstants;

    public ContentController(
        ILogger<ContentController> logger,
        IContentPluginRegistry pluginRegistry,
        IContentRepository contentRepository,
        IAmazonS3 s3Client,
        IOptions<AmazonWebServicesConstants> awsConstants)
    {
        _logger = logger;
        _pluginRegistry = pluginRegistry;
        _contentRepository = contentRepository;
        _s3Client = s3Client;
        _awsConstants = awsConstants?.Value ?? new AmazonWebServicesConstants();
    }

    // ────────────────────────── UPLOAD ──────────────────────────
    [HttpPost("upload-init")]
    [Authorize(Roles = "Admin")]
    public IActionResult UploadInit([FromRoute] string contentType, [FromBody] ContentUploadInitRequest request)
    {
        var plugin = _pluginRegistry.Get(contentType);
        if (plugin == null)
            return NotFound($"Unknown content type: {contentType}");

        if (request?.Item == null)
            return BadRequest("Invalid upload request.");

        request.Item.ContentType = plugin.ContentType;
        var validationError = plugin.ValidateMetadata(request.Item.Metadata);
        if (validationError != null)
            return BadRequest(validationError);

        var storagePath = plugin.BuildStoragePath(request.Item.Metadata);
        if (string.IsNullOrWhiteSpace(storagePath))
            return BadRequest("Unable to build storage path from metadata.");

        request.Item.StoragePath = storagePath;
        var mimeType = plugin.ResolveMimeType(request.Item.Metadata);
        request.Item.Format = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;

        var urlExpiryMinutes = NormalizeExpiryMinutes(request.UrlExpiresInMinutes);
        var expiresAt = DateTime.UtcNow.AddMinutes(urlExpiryMinutes);
        var uploadUrl = _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _awsConstants.BucketName,
            Key = storagePath,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = request.Item.Format
        });

        _logger.LogInformation("[{ContentType}] Presigned upload URL generated for '{Title}' at '{Path}'",
            contentType, request.Item.Title, storagePath);

        return Ok(new ContentUploadInitResponse
        {
            Item = request.Item,
            UploadUrl = uploadUrl,
            ExpiresAtUtc = expiresAt,
            RequiredHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = request.Item.Format
            }
        });
    }

    [HttpPost("upload-complete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UploadComplete([FromRoute] string contentType, [FromBody] ContentUploadCompleteRequest request)
    {
        var plugin = _pluginRegistry.Get(contentType);
        if (plugin == null)
            return NotFound($"Unknown content type: {contentType}");

        if (request?.Item == null)
            return BadRequest("Invalid upload-complete request.");

        if (string.IsNullOrWhiteSpace(request.Item.StoragePath))
            return BadRequest("storagePath is required.");

        request.Item.ContentType = plugin.ContentType;
        var validationError = plugin.ValidateMetadata(request.Item.Metadata);
        if (validationError != null)
            return BadRequest(validationError);

        var expectedStoragePath = plugin.BuildStoragePath(request.Item.Metadata);
        if (string.IsNullOrWhiteSpace(expectedStoragePath))
            return BadRequest("Unable to build storage path from metadata.");

        if (!string.Equals(expectedStoragePath, request.Item.StoragePath, StringComparison.Ordinal))
            return BadRequest("Storage path does not match metadata.");

        try
        {
            var head = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _awsConstants.BucketName,
                Key = request.Item.StoragePath
            });

            request.Item.SizeInBytes = head.ContentLength;
            request.Item.Format = string.IsNullOrWhiteSpace(head.Headers.ContentType)
                ? plugin.ResolveMimeType(request.Item.Metadata)
                : head.Headers.ContentType;
            request.Item.LastModified = DateTime.UtcNow;

            var existing = await _contentRepository.GetItemsAsync(plugin.TableName, 1, i => i.StoragePath == request.Item.StoragePath);
            var existingItem = existing.FirstOrDefault();

            if (existingItem != null)
            {
                request.Item.Id = existingItem.Id;
                request.Item.CreatedAt = existingItem.CreatedAt;
                var updated = await _contentRepository.UpdateAsync(request.Item, plugin.TableName);
                if (!updated)
                    return StatusCode(500, "Failed to update DB record.");

                return Ok(new { Message = "Upload finalized", Item = request.Item });
            }

            var created = await _contentRepository.CreateAsync(request.Item, plugin.TableName);
            if (!created)
                return StatusCode(500, "Failed to create DB record.");

            return Ok(new { Message = "Upload finalized", Item = request.Item });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Upload complete failed. Object not found at '{Path}'.", request.Item.StoragePath);
            return BadRequest("Upload not found in storage. Complete the direct upload before finalizing metadata.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finalizing upload for {Title}", request.Item.Title);
            return StatusCode(500, "Internal server error");
        }
    }

    // ────────────────────────── GET ITEMS (metadata only) ──────────────────────────
    [HttpGet("items")]
    [Authorize(Roles = "User,Admin")]
    public async Task<IActionResult> GetItems(
        [FromRoute] string contentType,
        [FromQuery] int? limit,
        [FromQuery] string? titlePrefix,
        [FromQuery] string? lastModifiedSince,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var plugin = _pluginRegistry.Get(contentType);
        if (plugin == null)
            return NotFound($"Unknown content type: {contentType}");

        var lastModifiedSinceDate = ParseDate(lastModifiedSince);

        // Collect all query params for plugin-specific filtering
        var queryParams = HttpContext.Request.Query
            .ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var pluginFilter = plugin.BuildFilter(queryParams!);

        Func<ContentItem, bool> combinedFilter = item =>
            (string.IsNullOrEmpty(titlePrefix) || (item.Title != null && item.Title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase))) &&
            MatchesLastModified(item.LastModified, lastModifiedSinceDate) &&
            pluginFilter(item);

        var items = await _contentRepository.GetItemsAsync(plugin.TableName, limit ?? int.MaxValue, combinedFilter);
        var paged = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(paged);
    }

    // ────────────────────────── DOWNLOAD (bulk) ──────────────────────────
    [HttpGet("download")]
    [Authorize(Roles = "User,Admin")]
    public async Task<IActionResult> Download(
        [FromRoute] string contentType,
        [FromQuery] int? limit,
        [FromQuery] string? titlePrefix,
        [FromQuery] string? lastModifiedSince,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] int urlExpiresInMinutes = DefaultUrlExpiryMinutes)
    {
        var plugin = _pluginRegistry.Get(contentType);
        if (plugin == null)
            return NotFound($"Unknown content type: {contentType}");

        var lastModifiedSinceDate = ParseDate(lastModifiedSince);
        var queryParams = HttpContext.Request.Query
            .ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var pluginFilter = plugin.BuildFilter(queryParams!);

        Func<ContentItem, bool> combinedFilter = item =>
            (string.IsNullOrEmpty(titlePrefix) || (item.Title != null && item.Title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase))) &&
            MatchesLastModified(item.LastModified, lastModifiedSinceDate) &&
            pluginFilter(item);

        var items = await _contentRepository.GetItemsAsync(plugin.TableName, limit ?? int.MaxValue, combinedFilter);
        var paged = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var expiry = NormalizeExpiryMinutes(urlExpiresInMinutes);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiry);

        var result = new List<ContentDownloadUrlResponse>();
        foreach (var item in paged)
        {
            string? downloadUrl = null;
            if (!string.IsNullOrEmpty(item.StoragePath))
            {
                try
                {
                    downloadUrl = BuildDownloadUrl(item.StoragePath, expiresAt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate download URL for '{Title}'.", item.Title);
                }
            }
            result.Add(new ContentDownloadUrlResponse { Item = item, DownloadUrl = downloadUrl, ExpiresAtUtc = downloadUrl == null ? null : expiresAt });
        }

        return Ok(result);
    }

    // ────────────────────────── DOWNLOAD SINGLE ──────────────────────────
    [HttpGet("download/{id}")]
    [Authorize(Roles = "User,Admin")]
    public async Task<IActionResult> DownloadById(
        [FromRoute] string contentType,
        [FromRoute] string id,
        [FromQuery] int urlExpiresInMinutes = DefaultUrlExpiryMinutes)
    {
        var plugin = _pluginRegistry.Get(contentType);
        if (plugin == null)
            return NotFound($"Unknown content type: {contentType}");

        if (!Guid.TryParse(id, out var itemId))
            return BadRequest("Invalid id.");

        var item = await _contentRepository.GetByIdAsync(itemId, plugin.TableName);
        if (item == null)
            return NotFound();

        string? downloadUrl = null;
        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrEmpty(item.StoragePath))
        {
            try
            {
                expiresAt = DateTime.UtcNow.AddMinutes(NormalizeExpiryMinutes(urlExpiresInMinutes));
                downloadUrl = BuildDownloadUrl(item.StoragePath, expiresAt.Value.UtcDateTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate download URL for '{Title}'.", item.Title);
            }
        }

        return Ok(new ContentDownloadUrlResponse { Item = item, DownloadUrl = downloadUrl, ExpiresAtUtc = expiresAt });
    }

    // ────────────────────────── EDIT ──────────────────────────
    [HttpPut("edit")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit([FromRoute] string contentType, [FromBody] ContentItem item)
    {
        var plugin = _pluginRegistry.Get(contentType);
        if (plugin == null)
            return NotFound($"Unknown content type: {contentType}");

        if (item == null || item.Id == Guid.Empty)
            return BadRequest("Invalid item metadata.");

        item.ContentType = plugin.ContentType;

        try
        {
            var updated = await _contentRepository.UpdateAsync(item, plugin.TableName);
            if (updated)
                return Ok(new { Message = "Update successful" });
            return StatusCode(500, "Failed to update metadata.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating item {Title}", item.Title);
            return StatusCode(500, "Internal server error");
        }
    }

    // ────────────────────────── DELETE ──────────────────────────
    [HttpDelete("delete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete([FromRoute] string contentType, [FromBody] ContentDeleteRequest request)
    {
        var plugin = _pluginRegistry.Get(contentType);
        if (plugin == null)
            return NotFound($"Unknown content type: {contentType}");

        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest("No items to delete.");

        var deleted = new List<string>();
        var failed = new List<string>();

        foreach (var item in request.Items)
        {
            bool binaryDeleted = false;
            bool metadataDeleted = false;

            await plugin.OnBeforeDeleteAsync(item);

            if (!string.IsNullOrEmpty(item.StoragePath))
            {
                try
                {
                    await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                    {
                        BucketName = _awsConstants.BucketName,
                        Key = item.StoragePath
                    });
                    binaryDeleted = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete binary for '{Title}'.", item.Title);
                }
            }

            try
            {
                metadataDeleted = await _contentRepository.DeleteAsync(item.Id, plugin.TableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete metadata for '{Title}'.", item.Title);
            }

            if (binaryDeleted && metadataDeleted)
            {
                deleted.Add(item.Title ?? item.Id.ToString());
            }
            else
            {
                failed.Add(item.Title ?? item.Id.ToString());
                if (binaryDeleted && !metadataDeleted)
                    _logger.LogWarning("Binary deleted but DB failed for '{Title}'.", item.Title);
                if (!binaryDeleted && metadataDeleted)
                {
                    try
                    {
                        await _contentRepository.CreateAsync(item, plugin.TableName);
                        _logger.LogInformation("Rollback: metadata restored for '{Title}'.", item.Title);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(rollbackEx, "Rollback failed for '{Title}'.", item.Title);
                    }
                }
            }
        }

        return Ok(new { Deleted = deleted, Failed = failed });
    }

    // ────────────────────────── LIST PLUGINS ──────────────────────────
    [HttpGet("/content/plugins")]
    [Authorize(Roles = "User,Admin")]
    public IActionResult ListPlugins()
    {
        var plugins = _pluginRegistry.GetAll()
            .Select(p => new { p.ContentType, p.DisplayName, AllowedMimeTypes = p.AllowedMimeTypes.ToList() });
        return Ok(plugins);
    }

    // ────────────────────────── Helpers ──────────────────────────
    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        return null;
    }

    private static bool MatchesLastModified(DateTime? lastModified, DateTimeOffset? since)
    {
        if (since == null) return true;
        if (lastModified == null) return false;
        var value = new DateTimeOffset(lastModified.Value, TimeSpan.Zero);
        return value >= since.Value;
    }

    private static int NormalizeExpiryMinutes(int inputMinutes)
    {
        if (inputMinutes <= 0)
            return DefaultUrlExpiryMinutes;

        return Math.Min(inputMinutes, MaxUrlExpiryMinutes);
    }

    private string BuildDownloadUrl(string storagePath, DateTime expiresAt)
    {
        return _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _awsConstants.BucketName,
            Key = storagePath,
            Verb = HttpVerb.GET,
            Expires = expiresAt
        });
    }
}
