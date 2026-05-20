using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NetManage.Api.Models;

public class ProjectTask
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("description")]
    [BsonIgnoreIfNull]
    public string? Description { get; set; }

    [BsonElement("assignedToUserId")]
    [BsonIgnoreIfNull]
    public string? AssignedToUserId { get; set; }

    [BsonElement("createdByUserId")]
    public string CreatedByUserId { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("priority")]
    public string Priority { get; set; } = string.Empty;

    [BsonElement("startDate")]
    [BsonIgnoreIfNull]
    public DateTime? StartDate { get; set; }

    [BsonElement("dueDate")]
    [BsonIgnoreIfNull]
    public DateTime? DueDate { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }
}
