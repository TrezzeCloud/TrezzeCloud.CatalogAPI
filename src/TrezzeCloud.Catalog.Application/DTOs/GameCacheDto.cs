namespace TrezzeCloud.Catalog.Application.DTOs;

public sealed record GameCacheDto(
    Guid Id,
    string Title,
    string Description,
    decimal Price,
    string Category,
    string ImageUrl,
    DateTime DisponibilizationDate,
    bool IsActive,
    DateTime CreatedAt);
