using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using SyncServer.Content.Models;

namespace SyncServer.Content.Repositories;

/// <summary>
/// Generic CRUD repository for ContentItem, parameterised by DynamoDB table name (from the plugin).
/// </summary>
public interface IContentRepository
{
    Task<bool> CreateAsync(ContentItem item, string tableName);
    Task<bool> DeleteAsync(Guid id, string tableName);
    Task<bool> UpdateAsync(ContentItem item, string tableName);
    Task<ContentItem?> GetByIdAsync(Guid id, string tableName);
    Task<IList<ContentItem>> GetItemsAsync(string tableName, int limit = 10, Func<ContentItem, bool>? filter = null);
}

public class ContentRepository : IContentRepository
{
    private readonly IDynamoDBContext _context;
    private readonly ILogger<ContentRepository> _logger;

    public ContentRepository(IDynamoDBContext context, ILogger<ContentRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    private static DynamoDBOperationConfig TableConfig(string tableName) =>
        new() { OverrideTableName = tableName };

    public async Task<bool> CreateAsync(ContentItem item, string tableName)
    {
        try
        {
            item.Id = Guid.NewGuid();
            item.CreatedAt = DateTime.UtcNow;
            item.LastModified = DateTime.UtcNow;
            await _context.SaveAsync(item, TableConfig(tableName));
            _logger.LogInformation("Created content item '{Title}' (Id: {Id}) in table {Table}.", item.Title, item.Id, tableName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create content item '{Title}' in table {Table}.", item.Title, tableName);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, string tableName)
    {
        try
        {
            await _context.DeleteAsync<ContentItem>(id, TableConfig(tableName));
            var deleted = await _context.LoadAsync<ContentItem>(id, new DynamoDBOperationConfig
            {
                OverrideTableName = tableName,
                ConsistentRead = true
            });
            var result = deleted == null;
            if (result)
                _logger.LogInformation("Deleted content item (Id: {Id}) from table {Table}.", id, tableName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete content item (Id: {Id}) from table {Table}.", id, tableName);
            return false;
        }
    }

    public async Task<bool> UpdateAsync(ContentItem item, string tableName)
    {
        try
        {
            item.LastModified = DateTime.UtcNow;
            await _context.SaveAsync(item, TableConfig(tableName));
            _logger.LogInformation("Updated content item '{Title}' (Id: {Id}) in table {Table}.", item.Title, item.Id, tableName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update content item '{Title}' (Id: {Id}) in table {Table}.", item.Title, item.Id, tableName);
            return false;
        }
    }

    public async Task<ContentItem?> GetByIdAsync(Guid id, string tableName)
    {
        try
        {
            return await _context.LoadAsync<ContentItem>(id, TableConfig(tableName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get content item (Id: {Id}) from table {Table}.", id, tableName);
            return null;
        }
    }

    public async Task<IList<ContentItem>> GetItemsAsync(string tableName, int limit = 10, Func<ContentItem, bool>? filter = null)
    {
        try
        {
            var scan = _context.ScanAsync<ContentItem>(new List<ScanCondition>(), TableConfig(tableName));
            var all = await scan.GetRemainingAsync();
            var list = all.AsQueryable();
            if (filter != null)
                list = list.Where(filter).AsQueryable();
            return list.Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan content items from table {Table}.", tableName);
            return new List<ContentItem>();
        }
    }
}
