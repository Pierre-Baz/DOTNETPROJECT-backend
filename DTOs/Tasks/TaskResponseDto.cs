namespace NetManage.Api.DTOs.Tasks;

public class TaskResponseDto
{
    public string Id { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskAssigneeDto? AssignedToUser { get; set; }

    public TaskAssigneeDto CreatedByUser { get; set; } = new();

    public string? EpicId { get; set; }

    public string? EpicName { get; set; }

    public string? SprintId { get; set; }

    public string? SprintName { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Priority { get; set; } = string.Empty;

    public DateTime? StartDate { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
