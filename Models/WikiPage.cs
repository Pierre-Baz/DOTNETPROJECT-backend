using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NetManage.Api.Models;

public class WikiPage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;

    [BsonElement("createdByUserId")]
    public string CreatedByUserId { get; set; } = string.Empty;

    [BsonElement("updatedByUserId")]
    [BsonIgnoreIfNull]
    public string? UpdatedByUserId { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }
}
