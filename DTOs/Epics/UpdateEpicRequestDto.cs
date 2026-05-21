using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Epics;

public class UpdateEpicRequestDto
{
    [Required]
    [MinLength(2)]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public string? SprintId { get; set; }
}
