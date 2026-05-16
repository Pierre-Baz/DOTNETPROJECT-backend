using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Projects;

public class UpdateProjectRequestDto
{
    [Required]
    [MinLength(2)]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }
}
