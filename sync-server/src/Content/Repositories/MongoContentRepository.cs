using MongoDB.Driver;
using SyncServer.Content.Models;

namespace SyncServer.Content.Repositories;

/// <summary>
/// MongoDB-backed <see cref="IContentRepository"/>. Mirrors the DynamoDB implementation's
/// contract: the collection name is the plugin's <c>TableName</c>, so the two backends are
/// drop-in interchangeable. Selected when <c>Database:Provider</c> is "MongoDb".
/// </summary>
public class MongoContentRepository : IContentRepository
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoContentRepository> _logger;

    public MongoContentRepository(IMongoDatabase database, ILogger<MongoContentRepository> logger)
    {
        _database = database;
        _logger = logger;
    }

    private IMongoCollection<ContentItem> Collection(string tableName) =>
        _database.GetCollection<ContentItem>(tableName);

    private static FilterDefinition<ContentItem> ById(Guid id) =>
        Builders<ContentItem>.Filter.Eq(x => x.Id, id);

    public async Task<bool> CreateAsync(ContentItem item, string tableName)
    {
        try
        {
            item.Id = Guid.NewGuid();
            item.CreatedAt = DateTime.UtcNow;
            item.LastModified = DateTime.UtcNow;
            await Collection(tableName).InsertOneAsync(item);
            _logger.LogInformation("Created content item '{Title}' (Id: {Id}) in collection {Collection}.", item.Title, item.Id, tableName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create content item '{Title}' in collection {Collection}.", item.Title, tableName);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, string tableName)
    {
        try
        {
            var result = await Collection(tableName).DeleteOneAsync(ById(id));
            var deleted = result.IsAcknowledged && result.DeletedCount > 0;
            if (deleted)
                _logger.LogInformation("Deleted content item (Id: {Id}) from collection {Collection}.", id, tableName);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete content item (Id: {Id}) from collection {Collection}.", id, tableName);
            return false;
        }
    }

    public async Task<bool> UpdateAsync(ContentItem item, string tableName)
    {
        try
        {
            item.LastModified = DateTime.UtcNow;
            // Upsert to match the DynamoDB SaveAsync (put) semantics.
            await Collection(tableName).ReplaceOneAsync(ById(item.Id), item, new ReplaceOptions { IsUpsert = true });
            _logger.LogInformation("Updated content item '{Title}' (Id: {Id}) in collection {Collection}.", item.Title, item.Id, tableName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update content item '{Title}' (Id: {Id}) in collection {Collection}.", item.Title, item.Id, tableName);
            return false;
        }
    }

    public async Task<ContentItem?> GetByIdAsync(Guid id, string tableName)
    {
        try
        {
            return await Collection(tableName).Find(ById(id)).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get content item (Id: {Id}) from collection {Collection}.", id, tableName);
            return null;
        }
    }

    public async Task<IList<ContentItem>> GetItemsAsync(string tableName, int limit = 10, Func<ContentItem, bool>? filter = null)
    {
        try
        {
            // Match the DynamoDB scan-then-filter-then-take behaviour: the plugin filter is an
            // in-memory predicate, so we load the collection and apply it client-side.
            var all = await Collection(tableName).Find(Builders<ContentItem>.Filter.Empty).ToListAsync();
            IEnumerable<ContentItem> list = all;
            if (filter != null)
                list = list.Where(filter);
            return list.Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read content items from collection {Collection}.", tableName);
            return new List<ContentItem>();
        }
    }
}
