using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NetManage.Api.Models;

public class TaskComment
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [BsonElement("createdByUserId")]
    public string CreatedByUserId { get; set; } = string.Empty;

    [BsonElement("body")]
    public string Body { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
