using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Wiki;

public class UpdateWikiPageRequestDto
{
    [Required]
    [MinLength(2)]
    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(12000)]
    public string? Content { get; set; }
}
