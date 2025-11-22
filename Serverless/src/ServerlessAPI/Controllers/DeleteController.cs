using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using ServerlessAPI.Repositories;
using ServerlessAPI.Entities;
using ServerlessAPI.Abstraction;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;

namespace Lambda.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(Roles = "Admin")]
    public class DeleteController : ControllerBase
    {
        private readonly ILogger<DeleteController> _logger;
        private readonly IMusicRepository _musicRepository;
        private readonly IAmazonS3 _s3Client;
        private readonly ServerlessAPI.Abstraction.AmazonWebServicesConstants _awsConstants;

        public DeleteController(ILogger<DeleteController> logger, IMusicRepository musicRepository, IAmazonS3 s3Client, IOptions<ServerlessAPI.Abstraction.AmazonWebServicesConstants> awsConstant)
        {
            _logger = logger;
            _musicRepository = musicRepository;
            _s3Client = s3Client;
            _awsConstants = awsConstant?.Value ?? new ServerlessAPI.Abstraction.AmazonWebServicesConstants();
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] DeleteRequest request)
        {
            if (request?.Items == null || request.Items.Count == 0)
                return BadRequest("No items to delete.");

            var deletedItems = new List<Music>();
            var failedItems = new List<string>();

            foreach (var music in request.Items)
            {
                bool binaryDeleted = false;
                bool metadataDeleted = false;

                // Delete binary from S3
                if (!string.IsNullOrEmpty(music.StoragePath))
                {
                    try
                    {
                        var deleteObjectRequest = new DeleteObjectRequest
                        {
                            BucketName = _awsConstants.BucketName, // Replace with your bucket name or fetch from config
                            Key = music.StoragePath
                        };
                        await _s3Client.DeleteObjectAsync(deleteObjectRequest);
                        binaryDeleted = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to delete binary for song {music.Title}.");
                    }
                }

                // Delete metadata from DB
                try
                {
                    metadataDeleted = await _musicRepository.DeleteAsync(music);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to delete metadata for song {music.Title}.");
                }

                if (binaryDeleted && metadataDeleted)
                {
                    deletedItems.Add(music);
                }
                else
                {
                    failedItems.Add(music.Title ?? music.Id.ToString());
                    // Rollback: restore binary and/or metadata if needed
                    if (binaryDeleted && !metadataDeleted)
                    {
                        // Rollback binary: not possible unless you have a backup
                        _logger.LogWarning($"Binary deleted but metadata failed for {music.Title}. Manual restore may be required.");
                    }
                    if (!binaryDeleted && metadataDeleted)
                    {
                        // Rollback metadata: try to restore
                        try
                        {
                            await _musicRepository.CreateAsync(music);
                            _logger.LogInformation($"Rollback: metadata restored for {music.Title}.");
                        }
                        catch (Exception rollbackEx)
                        {
                            _logger.LogError(rollbackEx, $"Rollback failed for metadata of {music.Title}.");
                        }
                    }
                }
            }

            return Ok(new {
                Deleted = deletedItems.Select(m => m.Title ?? m.Id.ToString()).ToList(),
                Failed = failedItems
            });
        }
    }
}
