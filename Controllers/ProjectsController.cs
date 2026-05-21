using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NetManage.Api.DTOs.Projects;
using NetManage.Api.Models;
using NetManage.Api.Services;

namespace NetManage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly MongoDbContext _mongoDbContext;

    public ProjectsController(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    [HttpGet]
    [ProducesResponseType<List<ProjectResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ProjectResponseDto>>> GetProjects(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var projects = await _mongoDbContext.Projects
            .Find(project => project.MemberIds.Contains(currentUserId))
            .SortByDescending(project => project.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(await MapProjectsToResponses(projects, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponseDto>> CreateProject(
        CreateProjectRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ValidateRequiredText(request.Name, nameof(request.Name)))
        {
            return ValidationProblem(ModelState);
        }

        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var currentUser = await FindUserById(currentUserId, cancellationToken);

        if (currentUser is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = NormalizeOptionalText(request.Description),
            OwnerId = currentUser.Id,
            MemberIds = new List<string> { currentUser.Id },
            CreatedAt = DateTime.UtcNow
        };

        await _mongoDbContext.Projects.InsertOneAsync(project, cancellationToken: cancellationToken);

        var response = await MapProjectToResponse(project, cancellationToken);
        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponseDto>> GetProject(string id, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var project = await FindProjectById(id, cancellationToken);

        if (project is null)
        {
            return NotFound(new { message = "Project not found." });
        }

        if (!EnsureProjectMember(project, currentUserId))
        {
            return Forbid();
        }

        return Ok(await MapProjectToResponse(project, cancellationToken));
    }

    [HttpPut("{id}")]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponseDto>> UpdateProject(
        string id,
        UpdateProjectRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ValidateRequiredText(request.Name, nameof(request.Name)))
        {
            return ValidationProblem(ModelState);
        }

        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var project = await FindProjectById(id, cancellationToken);

        if (project is null)
        {
            return NotFound(new { message = "Project not found." });
        }

        if (!EnsureProjectOwner(project, currentUserId))
        {
            return Forbid();
        }

        project.Name = request.Name.Trim();
        project.Description = NormalizeOptionalText(request.Description);
        project.UpdatedAt = DateTime.UtcNow;

        await _mongoDbContext.Projects.ReplaceOneAsync(
            existingProject => existingProject.Id == project.Id,
            project,
            cancellationToken: cancellationToken);

        return Ok(await MapProjectToResponse(project, cancellationToken));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProject(string id, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var project = await FindProjectById(id, cancellationToken);

        if (project is null)
        {
            return NotFound(new { message = "Project not found." });
        }

        if (!EnsureProjectOwner(project, currentUserId))
        {
            return Forbid();
        }

        await _mongoDbContext.Projects.DeleteOneAsync(
            existingProject => existingProject.Id == project.Id,
            cancellationToken);

        await _mongoDbContext.Tasks.DeleteManyAsync(
            task => task.ProjectId == project.Id,
            cancellationToken);

        await _mongoDbContext.WikiPages.DeleteManyAsync(
            page => page.ProjectId == project.Id,
            cancellationToken);

        await _mongoDbContext.Epics.DeleteManyAsync(
            epic => epic.ProjectId == project.Id,
            cancellationToken);

        await _mongoDbContext.Sprints.DeleteManyAsync(
            sprint => sprint.ProjectId == project.Id,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{id}/members")]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectResponseDto>> AddProjectMember(
        string id,
        AddProjectMemberRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            ModelState.AddModelError(nameof(request.Email), "Email is required.");
            return ValidationProblem(ModelState);
        }

        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var project = await FindProjectById(id, cancellationToken);

        if (project is null)
        {
            return NotFound(new { message = "Project not found." });
        }

        if (!EnsureProjectOwner(project, currentUserId))
        {
            return Forbid();
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _mongoDbContext.Users
            .Find(existingUser => existingUser.Email == normalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        if (project.MemberIds.Contains(user.Id))
        {
            return Conflict(new { message = "User is already a project member." });
        }

        project.MemberIds.Add(user.Id);
        project.UpdatedAt = DateTime.UtcNow;

        var update = Builders<Project>.Update
            .AddToSet(existingProject => existingProject.MemberIds, user.Id)
            .Set(existingProject => existingProject.UpdatedAt, project.UpdatedAt);

        await _mongoDbContext.Projects.UpdateOneAsync(
            existingProject => existingProject.Id == project.Id,
            update,
            cancellationToken: cancellationToken);

        return Ok(await MapProjectToResponse(project, cancellationToken));
    }

    [HttpDelete("{id}/members/{userId}")]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponseDto>> RemoveProjectMember(
        string id,
        string userId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var project = await FindProjectById(id, cancellationToken);

        if (project is null)
        {
            return NotFound(new { message = "Project not found." });
        }

        if (!EnsureProjectOwner(project, currentUserId))
        {
            return Forbid();
        }

        if (project.OwnerId == userId)
        {
            return BadRequest(new { message = "Project owner cannot be removed from the project." });
        }

        var user = await FindUserById(userId, cancellationToken);

        if (user is null || !project.MemberIds.Contains(user.Id))
        {
            return NotFound(new { message = "Project member not found." });
        }

        project.MemberIds.Remove(user.Id);
        project.UpdatedAt = DateTime.UtcNow;

        var update = Builders<Project>.Update
            .Pull(existingProject => existingProject.MemberIds, user.Id)
            .Set(existingProject => existingProject.UpdatedAt, project.UpdatedAt);

        await _mongoDbContext.Projects.UpdateOneAsync(
            existingProject => existingProject.Id == project.Id,
            update,
            cancellationToken: cancellationToken);

        return Ok(await MapProjectToResponse(project, cancellationToken));
    }

    [HttpGet("{id}/members")]
    [ProducesResponseType<List<ProjectMemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<ProjectMemberDto>>> GetProjectMembers(
        string id,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var project = await FindProjectById(id, cancellationToken);

        if (project is null)
        {
            return NotFound(new { message = "Project not found." });
        }

        if (!EnsureProjectMember(project, currentUserId))
        {
            return Forbid();
        }

        return Ok(await MapProjectMembers(project, cancellationToken));
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
    }

    private async Task<Project?> FindProjectById(string id, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        return await _mongoDbContext.Projects
            .Find(project => project.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<User?> FindUserById(string userId, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return null;
        }

        return await _mongoDbContext.Users
            .Find(user => user.Id == userId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool EnsureProjectMember(Project project, string userId)
    {
        return project.MemberIds.Contains(userId);
    }

    private static bool EnsureProjectOwner(Project project, string userId)
    {
        return project.OwnerId == userId;
    }

    private async Task<List<ProjectResponseDto>> MapProjectsToResponses(
        List<Project> projects,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForProjects(projects, cancellationToken);
        return projects.Select(project => MapProjectToResponse(project, usersById)).ToList();
    }

    private async Task<ProjectResponseDto> MapProjectToResponse(
        Project project,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForProjects(new List<Project> { project }, cancellationToken);
        return MapProjectToResponse(project, usersById);
    }

    private static ProjectResponseDto MapProjectToResponse(
        Project project,
        IReadOnlyDictionary<string, User> usersById)
    {
        usersById.TryGetValue(project.OwnerId, out var owner);

        return new ProjectResponseDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            OwnerId = project.OwnerId,
            OwnerName = owner?.FullName ?? string.Empty,
            Members = MapProjectMembers(project, usersById),
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    private async Task<List<ProjectMemberDto>> MapProjectMembers(
        Project project,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForProjects(new List<Project> { project }, cancellationToken);
        return MapProjectMembers(project, usersById);
    }

    private static List<ProjectMemberDto> MapProjectMembers(
        Project project,
        IReadOnlyDictionary<string, User> usersById)
    {
        return project.MemberIds
            .Where(usersById.ContainsKey)
            .Select(memberId =>
            {
                var user = usersById[memberId];
                return new ProjectMemberDto
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email
                };
            })
            .ToList();
    }

    private async Task<IReadOnlyDictionary<string, User>> LoadUsersForProjects(
        List<Project> projects,
        CancellationToken cancellationToken)
    {
        var userIds = projects
            .SelectMany(project => project.MemberIds.Append(project.OwnerId))
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return new Dictionary<string, User>();
        }

        var users = await _mongoDbContext.Users
            .Find(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        return users.ToDictionary(user => user.Id);
    }

    private bool ValidateRequiredText(string value, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        ModelState.AddModelError(fieldName, $"{fieldName} is required.");
        return false;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
