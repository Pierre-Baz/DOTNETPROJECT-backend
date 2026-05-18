using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Tasks;

public class UpdateTaskStatusRequestDto
{
    [Required]
    public string Status { get; set; } = string.Empty;
}
