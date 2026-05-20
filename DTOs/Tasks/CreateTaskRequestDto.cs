using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Tasks;

public class CreateTaskRequestDto
{
    [Required]
    [MinLength(2)]
    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public string? AssignedToUserId { get; set; }

    [Required]
    public string Priority { get; set; } = string.Empty;

    public DateTime? StartDate { get; set; }

    public DateTime? DueDate { get; set; }
}
