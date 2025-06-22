using Amazon.DynamoDBv2.Model;
using Amazon.S3.Model;
using Lambda.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServerlessAPI.Entities;
using ServerlessAPI.Repositories;


namespace Lambda.Controllers
{
    [ApiController]
    [Route("[controller]")]
    internal class UploadController : ControllerBase
    {
        private readonly ILogger<UploadController> _logger;
        private readonly IOptions<AmazonWebServicesConstants> _awsConstants;
        private readonly IMusicRepository _musicRepository;
        public UploadController(ILogger<UploadController> logger, IOptions<AmazonWebServicesConstants> awsConstants, IMusicRepository musicRepository)
        {
            _awsConstants = awsConstants;
            _logger = logger;
            _musicRepository = musicRepository;
        }
        [HttpPost("upload")]
        public async Task<IActionResult> Upload(UploadRequest uploadRequestModel)
        {
            _logger.LogInformation($"Upload request recieved for new song: {uploadRequestModel?.Metadata?.Title} with key: {uploadRequestModel?.Metadata?.StoragePath}");

            if (uploadRequestModel?.Metadata == null || string.IsNullOrEmpty(uploadRequestModel.Binary))
            {
                return BadRequest("Invalid upload request.");
            }

            var putRequest = new PutObjectRequest
            {
                BucketName = _awsConstants.Value.BucketName,
                Key = uploadRequestModel.Metadata.StoragePath,
                InputStream = new MemoryStream(Convert.FromBase64String(uploadRequestModel.Binary)),
                ContentType = uploadRequestModel.Metadata.Format.ToString().ToLowerInvariant(),
            };

            Amazon.S3.AmazonS3Client? client = null;
            bool dbCreated = false;
            try
            {
                // 1. Create DB record
                dbCreated = await _musicRepository.CreateAsync(uploadRequestModel.Metadata);
                if (!dbCreated)
                {
                    _logger.LogError($"Failed to create DB record for song {uploadRequestModel.Metadata.Title}.");
                    return StatusCode(500, "Failed to create DB record.");
                }

                // 2. Upload to S3
                client = new Amazon.S3.AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(_awsConstants.Value.Region));
                var response = await client.PutObjectAsync(putRequest);
                if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.LogInformation($"Song {uploadRequestModel.Metadata.Title} uploaded successfully.");
                    return Ok(new { Message = "Upload successful" });
                }
                else
                {
                    // S3 upload failed, rollback DB
                    await _musicRepository.DeleteAsync(uploadRequestModel.Metadata);
                    _logger.LogError($"Failed to upload song {uploadRequestModel.Metadata.Title}. Status code: {response.HttpStatusCode}");
                    return StatusCode((int)response.HttpStatusCode, "Upload failed");
                }
            }
            catch (Exception ex)
            {
                // Rollback DB if S3 upload failed after DB insert
                if (dbCreated)
                {
                    try
                    {
                        await _musicRepository.DeleteAsync(uploadRequestModel.Metadata);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(rollbackEx, $"Rollback failed for song {uploadRequestModel.Metadata.Title}");
                    }
                }
                _logger.LogError(ex, $"An error occurred while uploading song {uploadRequestModel.Metadata.Title}");
                return StatusCode(500, "Internal server error");
            }
            finally
            {
                client?.Dispose();
            }
        }
    }
}
