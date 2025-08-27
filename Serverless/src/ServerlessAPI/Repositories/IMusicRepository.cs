using ServerlessAPI.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServerlessAPI.Repositories;

/// <summary>
/// Sample DynamoDB Table book CRUD
/// </summary>
public interface IMusicRepository
{
    /// <summary>
    /// Include new book to the DynamoDB Table
    /// </summary>
    /// <param name="book">book to include</param>
    /// <returns>success/failure</returns>
    Task<bool> CreateAsync(Music music);
    
    /// <summary>
    /// Remove existing book from DynamoDB Table
    /// </summary>
    /// <param name="book">book to remove</param>
    /// <returns></returns>
    Task<bool> DeleteAsync(Music music);

    /// <summary>
    /// List book from DynamoDb Table with items limit (default=10)
    /// </summary>
    /// <param name="limit">limit (default=10)</param>
    /// <param name="filter">optional filter for more efficient querying</param>
    /// <returns>Collection of books</returns>
    Task<IList<Music>> GetMusicAsync(int limit = 10, Func<Music, bool>? filter = null);

    /// <summary>
    /// Get book by PK
    /// </summary>
    /// <param name="id">book`s PK</param>
    /// <returns>Book object</returns>
    Task<Music?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Update book content
    /// </summary>
    /// <param name="book">book to be updated</param>
    /// <returns></returns>
    Task<bool> UpdateAsync(Music music);
}