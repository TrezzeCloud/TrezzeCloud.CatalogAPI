using MongoDB.Driver;
using TrezzeCloud.Catalog.Domain.Entities;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Documents;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Interfaces;

namespace TrezzeCloud.Catalog.Infrastructure.MongoDb.Repositories;

public class GameReviewRepository : IGameReviewRepository
{
    private readonly IMongoCollection<GameReviewDocument> _collection;

    public GameReviewRepository(MongoDbContext context)
    {
        _collection = context.Database
            .GetCollection<GameReviewDocument>("gameReviews");
    }

    public async Task<GameReview> CreateAsync(GameReview review)
    {
        var document = new GameReviewDocument
        {
            GameId = review.GameId,
            UserId = review.UserId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = DateTime.UtcNow
        };

        await _collection.InsertOneAsync(document);

        review.Id = document.Id;
        review.CreatedAt = document.CreatedAt;

        return review;
    }

    public async Task<List<GameReview>> GetByGameIdAsync(Guid gameId)
    {
        var documents = await _collection
            .Find(x => x.GameId == gameId)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();

        return documents.Select(Map).ToList();
    }

    public async Task<GameReview?> GetByIdAsync(string id)
    {
        var document = await _collection
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync();

        return document is null ? null : Map(document);
    }

    public async Task UpdateAsync(GameReview review)
    {
        var update = Builders<GameReviewDocument>.Update
            .Set(x => x.Rating, review.Rating)
            .Set(x => x.Comment, review.Comment)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        await _collection.UpdateOneAsync(
            x => x.Id == review.Id,
            update);
    }

    public async Task DeleteAsync(string id)
    {
        await _collection.DeleteOneAsync(x => x.Id == id);
    }

    private static GameReview Map(GameReviewDocument document)
    {
        return new GameReview
        {
            Id = document.Id,
            GameId = document.GameId,
            UserId = document.UserId,
            Rating = document.Rating,
            Comment = document.Comment,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt
        };
    }
}