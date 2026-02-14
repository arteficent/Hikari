using Amazon.S3;
using Amazon.S3.Model;
using ServerAPI.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServerAPI.Repositories;


namespace ServerAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(Roles = "Admin")]
    public class UploadController : ControllerBase
    {
        private readonly ILogger<UploadController> _logger;
        private readonly IMusicRepository _musicRepository;
        private readonly IAmazonS3 _client;
        private readonly ServerAPI.Abstraction.AmazonWebServicesConstants _awsConstants;

        public UploadController(ILogger<UploadController> logger, IOptions<ServerAPI.Abstraction.AmazonWebServicesConstants> awsConstants, IMusicRepository musicRepository, IAmazonS3 client)
        {
            _awsConstants = awsConstants?.Value ?? new ServerAPI.Abstraction.AmazonWebServicesConstants();
            _logger = logger;
            _musicRepository = musicRepository;
            _client = client;
        }

        [HttpPost]
        public async Task<IActionResult> Upload(UploadRequest uploadRequestModel)
        {
            _logger.LogInformation("Upload request received for new song: {Title} with key: {StoragePath}", uploadRequestModel?.Metadata?.Title, uploadRequestModel?.Metadata?.StoragePath);

            if (uploadRequestModel?.Metadata == null || string.IsNullOrEmpty(uploadRequestModel.SongBinary))
            {
                return BadRequest("Invalid upload request.");
            }

            // Check if song with the same StoragePath already exists
            var existingSongs = await _musicRepository.GetMusicAsync(1, m => m.StoragePath == uploadRequestModel.Metadata.StoragePath);
            bool songExists = existingSongs.Any();

            var putSongRequest = new PutObjectRequest
            {
                BucketName = _awsConstants.BucketName,
                Key = uploadRequestModel.Metadata.StoragePath,
                InputStream = new MemoryStream(Convert.FromBase64String(uploadRequestModel.SongBinary)),
                ContentType = ContentTypeExtensions.ToMimeString(uploadRequestModel.Metadata.MusicFormat)
            };

            bool dbCreated = false;
            try
            {
                if (!songExists)
                {
                    // 1. Create DB record  
                    dbCreated = await _musicRepository.CreateAsync(uploadRequestModel.Metadata);
                    if (!dbCreated)
                    {
                        _logger.LogError("Failed to create DB record for song {Title}.", uploadRequestModel.Metadata.Title);
                        return StatusCode(500, "Failed to create DB record.");
                    }
                }
                else
                {
                    _logger.LogInformation("Song with storage path {StoragePath} already exists. Skipping DB creation.", uploadRequestModel.Metadata.StoragePath);
                }

                // 2. Upload to S3  
                var songResponse = await _client.PutObjectAsync(putSongRequest);
                if (songResponse.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.LogInformation("Song for {Title} uploaded successfully.", uploadRequestModel.Metadata.Title);
                    return Ok(new { Message = songExists ? "Song binary updated" : "Upload successful" });
                }
                else
                {
                    // S3 upload failed, rollback DB  
                    if (!songExists)
                        await _musicRepository.DeleteAsync(uploadRequestModel.Metadata);
                    _logger.LogError("Failed to upload song for {Title}. Status code: {StatusCode}", uploadRequestModel.Metadata.Title, songResponse.HttpStatusCode);
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
                        _logger.LogError(rollbackEx, "Rollback failed for song {Title}", uploadRequestModel.Metadata.Title);
                    }
                }
                _logger.LogError(ex, "An error occurred while uploading song {Title}", uploadRequestModel.Metadata.Title);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
