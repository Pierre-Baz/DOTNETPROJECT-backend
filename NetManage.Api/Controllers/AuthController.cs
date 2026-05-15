using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NetManage.Api.DTOs.Auth;
using NetManage.Api.Models;
using NetManage.Api.Services;

namespace NetManage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JwtService _jwtService;
    private readonly MongoDbContext _mongoDbContext;
    private readonly PasswordService _passwordService;

    public AuthController(
        JwtService jwtService,
        MongoDbContext mongoDbContext,
        PasswordService passwordService)
    {
        _jwtService = jwtService;
        _mongoDbContext = mongoDbContext;
        _passwordService = passwordService;
    }

    [HttpPost("register")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            ModelState.AddModelError(nameof(request.FullName), "FullName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            ModelState.AddModelError(nameof(request.Email), "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            ModelState.AddModelError(nameof(request.Password), "Password is required.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existingUser = await _mongoDbContext.Users
            .Find(user => user.Email == normalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingUser is not null)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = _passwordService.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _mongoDbContext.Users.InsertOneAsync(user, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        return Ok(BuildAuthResponse(user));
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            ModelState.AddModelError(nameof(request.Email), "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            ModelState.AddModelError(nameof(request.Password), "Password is required.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _mongoDbContext.Users
            .Find(existingUser => existingUser.Email == normalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null || !_passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(BuildAuthResponse(user));
    }

    private AuthResponseDto BuildAuthResponse(User user)
    {
        return new AuthResponseDto
        {
            Token = _jwtService.GenerateToken(user),
            User = new AuthUserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email
            }
        };
    }
}
