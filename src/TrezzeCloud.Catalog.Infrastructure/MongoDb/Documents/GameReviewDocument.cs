using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace TrezzeCloud.Catalog.Infrastructure.MongoDb.Documents;

public class GameReviewDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid GameId { get; set; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid UserId { get; set; }

    public int Rating { get; set; }

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}