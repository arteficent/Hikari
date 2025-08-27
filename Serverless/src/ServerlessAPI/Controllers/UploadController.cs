using Amazon;
using Amazon.S3.Model;
using Lambda.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServerlessAPI.Repositories;


namespace Lambda.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly ILogger<UploadController> _logger;
        private readonly IOptions<AmazonWebServicesConstants> _awsConstants;
        private readonly IMusicRepository _musicRepository;
        private readonly string _regionName;

        public UploadController(ILogger<UploadController> logger, IOptions<AmazonWebServicesConstants> awsConstants, IMusicRepository musicRepository)
        {
            _awsConstants = awsConstants;
            _logger = logger;
            _musicRepository = musicRepository;
            _regionName = Environment.GetEnvironmentVariable("AWS_REGION") ?? RegionEndpoint.APSouth1.SystemName;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(UploadRequest uploadRequestModel)
        {
            _logger.LogInformation($"Upload request received for new song: {uploadRequestModel?.Metadata?.Title} with key: {uploadRequestModel?.Metadata?.StoragePath}");

            if (uploadRequestModel?.Metadata == null || string.IsNullOrEmpty(uploadRequestModel.SongBinary))
            {
                return BadRequest("Invalid upload request.");
            }

            var putSongRequest = new PutObjectRequest
            {
                BucketName = _awsConstants.Value.BucketName,
                Key = uploadRequestModel.Metadata.StoragePath,
                InputStream = new MemoryStream(Convert.FromBase64String(uploadRequestModel.SongBinary)),
                ContentType = ContentTypeExtensions.ToMimeString(uploadRequestModel.Metadata.MusicFormat)
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
                client = new Amazon.S3.AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(_regionName));
                var songResponse = await client.PutObjectAsync(putSongRequest);
                if (songResponse.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.LogInformation($"Song for {uploadRequestModel.Metadata.Title} uploaded successfully.");
                    return Ok(new { Message = "Upload successful" });
                }
                else
                {
                    // S3 upload failed, rollback DB  
                    await _musicRepository.DeleteAsync(uploadRequestModel.Metadata);
                    _logger.LogError($"Failed to upload song for {uploadRequestModel.Metadata.Title}. Status codes: Song={songResponse.HttpStatusCode}");
                    return StatusCode(500, "Upload failed");
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
