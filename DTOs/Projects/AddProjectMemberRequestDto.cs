using System.ComponentModel.DataAnnotations;

namespace NetManage.Api.DTOs.Projects;

public class AddProjectMemberRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
