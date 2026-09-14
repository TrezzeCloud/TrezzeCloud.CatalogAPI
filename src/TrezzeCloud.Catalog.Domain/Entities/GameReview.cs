namespace TrezzeCloud.Catalog.Domain.Entities;

public class GameReview
{
    public string Id { get; set; } = string.Empty;

    public Guid GameId { get; set; }

    public Guid UserId { get; set; }

    public int Rating { get; set; }

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}