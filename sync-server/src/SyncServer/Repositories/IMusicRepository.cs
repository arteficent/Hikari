using SyncServer.Entities;

namespace SyncServer.Repositories;

/// <summary>
/// Music DynamoDB CRUD operations
/// </summary>
public interface IMusicRepository
{
    /// <summary>
    /// Add new music entry to the DynamoDB Table
    /// </summary>
    /// <param name="music">music to include</param>
    /// <returns>success/failure</returns>
    Task<bool> CreateAsync(Music music);
    
    /// <summary>
    /// Remove existing music from DynamoDB Table
    /// </summary>
    /// <param name="music">music to remove</param>
    /// <returns></returns>
    Task<bool> DeleteAsync(Music music);

    /// <summary>
    /// List music from DynamoDB Table with optional filtering
    /// </summary>
    /// <param name="limit">maximum number of items</param>
    /// <param name="filter">optional filter predicate</param>
    /// <returns>Collection of music</returns>
    Task<IList<Music>> GetMusicAsync(int limit = 10, Func<Music, bool>? filter = null);

    /// <summary>
    /// Get music by primary key
    /// </summary>
    /// <param name="id">music PK</param>
    /// <returns>Music object</returns>
    Task<Music?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Update music metadata
    /// </summary>
    /// <param name="music">music to be updated</param>
    /// <returns></returns>
    Task<bool> UpdateAsync(Music music);
}