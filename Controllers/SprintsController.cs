using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NetManage.Api.DTOs.Sprints;
using NetManage.Api.Models;
using NetManage.Api.Services;

namespace NetManage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId}/sprints")]
public class SprintsController : ControllerBase
{
    private static readonly Dictionary<string, string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Planned"] = "Planned",
        ["Active"] = "Active",
        ["Completed"] = "Completed"
    };

    private readonly MongoDbContext _mongoDbContext;

    public SprintsController(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    [HttpGet]
    [ProducesResponseType<List<SprintResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<SprintResponseDto>>> GetSprints(
        string projectId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var sprints = await _mongoDbContext.Sprints
            .Find(sprint => sprint.ProjectId == projectResult.Value!.Id)
            .SortByDescending(sprint => sprint.StartDate)
            .ToListAsync(cancellationToken);

        return Ok(sprints.Select(MapSprintToResponse).ToList());
    }

    [HttpPost]
    [ProducesResponseType<SprintResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SprintResponseDto>> CreateSprint(
        string projectId,
        CreateSprintRequestDto request,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        if (!ValidateSprintDetails(request.Name, request.StartDate, request.EndDate, request.Status, out var status))
        {
            return ValidationProblem(ModelState);
        }

        var activeConflict = await ValidateActiveSprint(projectResult.Value!.Id, null, status, cancellationToken);
        if (activeConflict is not null)
        {
            return activeConflict;
        }

        var sprint = new ProjectSprint
        {
            ProjectId = projectResult.Value!.Id,
            Name = request.Name.Trim(),
            Goal = NormalizeOptionalText(request.Goal),
            StartDate = request.StartDate!.Value,
            EndDate = request.EndDate!.Value,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

        await _mongoDbContext.Sprints.InsertOneAsync(sprint, cancellationToken: cancellationToken);

        return CreatedAtAction(
            nameof(GetSprint),
            new { projectId = sprint.ProjectId, sprintId = sprint.Id },
            MapSprintToResponse(sprint));
    }

    [HttpGet("{sprintId}")]
    [ProducesResponseType<SprintResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SprintResponseDto>> GetSprint(
        string projectId,
        string sprintId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var sprint = await FindSprintById(projectResult.Value!.Id, sprintId, cancellationToken);
        if (sprint is null)
        {
            return NotFound(new { message = "Sprint not found." });
        }

        return Ok(MapSprintToResponse(sprint));
    }

    [HttpPut("{sprintId}")]
    [ProducesResponseType<SprintResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SprintResponseDto>> UpdateSprint(
        string projectId,
        string sprintId,
        UpdateSprintRequestDto request,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var sprint = await FindSprintById(projectResult.Value!.Id, sprintId, cancellationToken);
        if (sprint is null)
        {
            return NotFound(new { message = "Sprint not found." });
        }

        if (!ValidateSprintDetails(request.Name, request.StartDate, request.EndDate, request.Status, out var status))
        {
            return ValidationProblem(ModelState);
        }

        var activeConflict = await ValidateActiveSprint(projectResult.Value!.Id, sprint.Id, status, cancellationToken);
        if (activeConflict is not null)
        {
            return activeConflict;
        }

        sprint.Name = request.Name.Trim();
        sprint.Goal = NormalizeOptionalText(request.Goal);
        sprint.StartDate = request.StartDate!.Value;
        sprint.EndDate = request.EndDate!.Value;
        sprint.Status = status;
        sprint.UpdatedAt = DateTime.UtcNow;

        await _mongoDbContext.Sprints.ReplaceOneAsync(
            existingSprint => existingSprint.Id == sprint.Id && existingSprint.ProjectId == sprint.ProjectId,
            sprint,
            cancellationToken: cancellationToken);

        return Ok(MapSprintToResponse(sprint));
    }

    [HttpDelete("{sprintId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSprint(
        string projectId,
        string sprintId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var sprint = await FindSprintById(projectResult.Value!.Id, sprintId, cancellationToken);
        if (sprint is null)
        {
            return NotFound(new { message = "Sprint not found." });
        }

        await _mongoDbContext.Sprints.DeleteOneAsync(
            existingSprint => existingSprint.Id == sprint.Id && existingSprint.ProjectId == sprint.ProjectId,
            cancellationToken);

        var update = Builders<ProjectEpic>.Update
            .Set(epic => epic.SprintId, null)
            .Set(epic => epic.UpdatedAt, DateTime.UtcNow);

        await _mongoDbContext.Epics.UpdateManyAsync(
            epic => epic.ProjectId == sprint.ProjectId && epic.SprintId == sprint.Id,
            update,
            cancellationToken: cancellationToken);

        return NoContent();
    }

    private async Task<ActionResult<Project>> LoadProjectForMember(
        string projectId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var project = await FindProjectById(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound(new { message = "Project not found." });
        }

        if (!project.MemberIds.Contains(currentUserId))
        {
            return Forbid();
        }

        return project;
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
    }

    private async Task<Project?> FindProjectById(string projectId, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(projectId, out _))
        {
            return null;
        }

        return await _mongoDbContext.Projects
            .Find(project => project.Id == projectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ProjectSprint?> FindSprintById(
        string projectId,
        string sprintId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(sprintId, out _))
        {
            return null;
        }

        return await _mongoDbContext.Sprints
            .Find(sprint => sprint.Id == sprintId && sprint.ProjectId == projectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private bool ValidateSprintDetails(
        string name,
        DateTime? startDate,
        DateTime? endDate,
        string? status,
        out string normalizedStatus)
    {
        normalizedStatus = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Name is required.");
        }

        if (!startDate.HasValue)
        {
            ModelState.AddModelError(nameof(startDate), "StartDate is required.");
        }

        if (!endDate.HasValue)
        {
            ModelState.AddModelError(nameof(endDate), "EndDate is required.");
        }

        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            ModelState.AddModelError(nameof(endDate), "EndDate cannot be before StartDate.");
        }

        if (!TryNormalizeStatus(status, out normalizedStatus))
        {
            ModelState.AddModelError(nameof(status), "Status must be one of: Planned, Active, Completed.");
        }

        return ModelState.IsValid;
    }

    private async Task<ActionResult?> ValidateActiveSprint(
        string projectId,
        string? currentSprintId,
        string status,
        CancellationToken cancellationToken)
    {
        if (!status.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var activeSprint = await _mongoDbContext.Sprints
            .Find(sprint =>
                sprint.ProjectId == projectId &&
                sprint.Status == "Active" &&
                sprint.Id != currentSprintId)
            .FirstOrDefaultAsync(cancellationToken);

        return activeSprint is null
            ? null
            : Conflict(new { message = "Only one sprint can be active for a project." });
    }

    private static bool TryNormalizeStatus(string? value, out string normalizedStatus)
    {
        normalizedStatus = string.Empty;
        var trimmedValue = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedValue) ||
            !AllowedStatuses.TryGetValue(trimmedValue, out var matchedStatus))
        {
            return false;
        }

        normalizedStatus = matchedStatus;
        return true;
    }

    private static SprintResponseDto MapSprintToResponse(ProjectSprint sprint)
    {
        return new SprintResponseDto
        {
            Id = sprint.Id,
            ProjectId = sprint.ProjectId,
            Name = sprint.Name,
            Goal = sprint.Goal,
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            Status = sprint.Status,
            CreatedAt = sprint.CreatedAt,
            UpdatedAt = sprint.UpdatedAt
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
