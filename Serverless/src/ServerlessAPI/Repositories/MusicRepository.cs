using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.Extensions.Options;
using ServerlessAPI.Entities;

namespace ServerlessAPI.Repositories;

public class MusicRepository : IMusicRepository
{
    private readonly IDynamoDBContext context;
    private readonly ILogger<MusicRepository> logger;
    public MusicRepository(IDynamoDBContext context, ILogger<MusicRepository> logger, IOptions<Lambda.Abstraction.AmazonWebServicesConstants> awsConstants)
    {
        this.context = context;
        this.logger = logger;
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

    public async Task<IList<Music>> GetMusicAsync(int limit = 10)
    {
        var result = new List<Music>();

        try
        {
            if (limit <= 0)
            {
                return result;
            }

            var filter = new ScanFilter();
            filter.AddCondition("Id", ScanOperator.IsNotNull);
            var scanConfig = new ScanOperationConfig()
            {
                Limit = limit,
                Filter = filter,
            };
            var queryResult = context.FromScanAsync<Music>(scanConfig);

            do
            {
                result.AddRange(await queryResult.GetNextSetAsync());
            }
            while (!queryResult.IsDone && result.Count < limit);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list music from DynamoDb Table.");
            return new List<Music>();
        }

        return result;
    }
}
