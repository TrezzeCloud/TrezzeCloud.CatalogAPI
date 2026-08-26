
using TrezzeCloud.Catalog.Domain.Entities;

namespace TrezzeCloud.Catalog.Infrastructure.MongoDb.Interfaces;

public interface IGameReviewRepository
{
    Task<GameReview> CreateAsync(GameReview review);

    Task<List<GameReview>> GetByGameIdAsync(Guid gameId);

    Task<GameReview?> GetByIdAsync(string id);

    Task UpdateAsync(GameReview review);

    Task DeleteAsync(string id);
}