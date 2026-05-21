using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NetManage.Api.Models;

public class ProjectEpic
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("sprintId")]
    [BsonIgnoreIfNull]
    public string? SprintId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    [BsonIgnoreIfNull]
    public string? Description { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }
}
