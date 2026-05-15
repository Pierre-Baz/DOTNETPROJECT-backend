namespace NetManage.Api.DTOs.Auth;

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;

    public AuthUserDto User { get; set; } = new();
}
