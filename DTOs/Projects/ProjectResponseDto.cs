namespace NetManage.Api.DTOs.Projects;

public class ProjectResponseDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;

    public List<ProjectMemberDto> Members { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
