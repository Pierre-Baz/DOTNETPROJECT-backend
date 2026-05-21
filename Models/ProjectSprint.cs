using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NetManage.Api.Models;

public class ProjectSprint
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("goal")]
    [BsonIgnoreIfNull]
    public string? Goal { get; set; }

    [BsonElement("startDate")]
    public DateTime StartDate { get; set; }

    [BsonElement("endDate")]
    public DateTime EndDate { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }
}
