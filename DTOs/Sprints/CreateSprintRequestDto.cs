using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Sprints;

public class CreateSprintRequestDto
{
    [Required]
    [MinLength(2)]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Goal { get; set; }

    [Required]
    public DateTime? StartDate { get; set; }

    [Required]
    public DateTime? EndDate { get; set; }

    [Required]
    public string Status { get; set; } = string.Empty;
}
