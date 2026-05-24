using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Tasks;

public class CreateTaskCommentRequestDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(1000)]
    public string Body { get; set; } = string.Empty;
}
