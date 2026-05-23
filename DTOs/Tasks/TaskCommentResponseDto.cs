namespace NetManage.Api.DTOs.Tasks;

public class TaskCommentResponseDto
{
    public string Id { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;

    public TaskAssigneeDto CreatedByUser { get; set; } = new();

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
