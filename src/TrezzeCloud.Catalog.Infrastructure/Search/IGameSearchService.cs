using TrezzeCloud.Catalog.Domain.Entities;

namespace TrezzeCloud.Catalog.Infrastructure.Search;

public interface IGameSearchService
{
    bool IsEnabled { get; }

    Task IndexAsync(Game game, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameSearchHit>> SearchAsync(
        string query, int limit, CancellationToken cancellationToken = default);
}

public sealed record GameSearchHit(
    Guid Id,
    string Title,
    string Description,
    string Category,
    decimal Price,
    string ImageUrl,
    DateTime DisponibilizationDate,
    double Score);
