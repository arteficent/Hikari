using Amazon.DynamoDBv2.DataModel;
using Microsoft.Extensions.Options;
using ServerlessAPI.Entities;
using ServerlessAPI.Abstraction;

namespace ServerlessAPI.Repositories;

public class MusicRepository : IMusicRepository
{
    private readonly IDynamoDBContext context;
    private readonly ILogger<MusicRepository> logger;
    private readonly Abstraction.AmazonWebServicesConstants awsConstants;

    public MusicRepository(IDynamoDBContext context, ILogger<MusicRepository> logger, IOptions<Abstraction.AmazonWebServicesConstants> awsOptions)
    {
        this.context = context;
        this.logger = logger;
        this.awsConstants = awsOptions != null ? awsOptions.Value : new Abstraction.AmazonWebServicesConstants();
    }

    public async Task<bool> CreateAsync(Music music)
    {
        try
        {
            music.Id = Guid.NewGuid();
            await context.SaveAsync<Music>(music);
            logger.LogInformation("New music '{Title}' (Id: {Id}) is added.", music.Title, music.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist music '{Title}' to DynamoDb Table.", music.Title);
            return false;
        }

        return true;
    }

    public async Task<bool> DeleteAsync(Music music)
    {
        bool result;
        try
        {
            await context.DeleteAsync<Music>(music.Id);
            Music deletedMusic = await context.LoadAsync<Music>(music.Id, new DynamoDBContextConfig
            {
                ConsistentRead = true,
            });
            result = deletedMusic == null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete music '{Title}' (Id: {Id}) from DynamoDb Table.", music.Title, music.Id);
            result = false;
        }

        if (result)
            logger.LogInformation("Music '{Title}' (Id: {Id}) is deleted.", music.Title, music.Id);

        return result;
    }

    public async Task<bool> UpdateAsync(Music music)
    {
        if (music == null) return false;

        try
        {
            await context.SaveAsync<Music>(music);
            logger.LogInformation("Music '{Title}' (Id: {Id}) is updated.", music.Title, music.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update music '{Title}' (Id: {Id}) in DynamoDb Table.", music?.Title, music?.Id);
            return false;
        }

        return true;
    }

    public async Task<Music?> GetByIdAsync(Guid id)
    {
        try
        {
            return await context.LoadAsync<Music>(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve music by Id: {Id} from DynamoDb Table.", id);
            return null;
        }
    }

    public async Task<IList<Music>> GetMusicAsync(int limit = 10, Func<Music, bool>? filter = null)
    {
        try
        {
            var scan = context.ScanAsync<Music>(new List<ScanCondition>());
            var all = await scan.GetRemainingAsync();
            var list = all.AsQueryable();
            if (filter != null)
                list = list.Where(filter).AsQueryable();
            return list.Take(limit).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scan music table.");
            return new List<Music>();
        }
    }
}
