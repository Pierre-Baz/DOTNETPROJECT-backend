using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NetManage.Api.DTOs.Epics;
using NetManage.Api.Models;
using NetManage.Api.Services;

namespace NetManage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId}/epics")]
public class EpicsController : ControllerBase
{
    private readonly MongoDbContext _mongoDbContext;

    public EpicsController(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    [HttpGet]
    [ProducesResponseType<List<EpicResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<EpicResponseDto>>> GetEpics(
        string projectId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var epics = await _mongoDbContext.Epics
            .Find(epic => epic.ProjectId == projectResult.Value!.Id)
            .SortByDescending(epic => epic.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(await MapEpicsToResponses(epics, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<EpicResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EpicResponseDto>> CreateEpic(
        string projectId,
        CreateEpicRequestDto request,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        if (!ValidateRequiredText(request.Name, nameof(request.Name)))
        {
            return ValidationProblem(ModelState);
        }

        var sprintIdResult = await NormalizeAndValidateSprintId(projectResult.Value!.Id, request.SprintId, cancellationToken);
        if (sprintIdResult.Result is not null)
        {
            return sprintIdResult.Result;
        }

        var epic = new ProjectEpic
        {
            ProjectId = projectResult.Value!.Id,
            SprintId = sprintIdResult.Value,
            Name = request.Name.Trim(),
            Description = NormalizeOptionalText(request.Description),
            CreatedAt = DateTime.UtcNow
        };

        await _mongoDbContext.Epics.InsertOneAsync(epic, cancellationToken: cancellationToken);

        var response = await MapEpicToResponse(epic, cancellationToken);
        return CreatedAtAction(nameof(GetEpic), new { projectId = epic.ProjectId, epicId = epic.Id }, response);
    }

    [HttpGet("{epicId}")]
    [ProducesResponseType<EpicResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EpicResponseDto>> GetEpic(
        string projectId,
        string epicId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var epic = await FindEpicById(projectResult.Value!.Id, epicId, cancellationToken);
        if (epic is null)
        {
            return NotFound(new { message = "Epic not found." });
        }

        return Ok(await MapEpicToResponse(epic, cancellationToken));
    }

    [HttpPut("{epicId}")]
    [ProducesResponseType<EpicResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EpicResponseDto>> UpdateEpic(
        string projectId,
        string epicId,
        UpdateEpicRequestDto request,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var epic = await FindEpicById(projectResult.Value!.Id, epicId, cancellationToken);
        if (epic is null)
        {
            return NotFound(new { message = "Epic not found." });
        }

        if (!ValidateRequiredText(request.Name, nameof(request.Name)))
        {
            return ValidationProblem(ModelState);
        }

        var sprintIdResult = await NormalizeAndValidateSprintId(projectResult.Value!.Id, request.SprintId, cancellationToken);
        if (sprintIdResult.Result is not null)
        {
            return sprintIdResult.Result;
        }

        epic.Name = request.Name.Trim();
        epic.Description = NormalizeOptionalText(request.Description);
        epic.SprintId = sprintIdResult.Value;
        epic.UpdatedAt = DateTime.UtcNow;

        await _mongoDbContext.Epics.ReplaceOneAsync(
            existingEpic => existingEpic.Id == epic.Id && existingEpic.ProjectId == epic.ProjectId,
            epic,
            cancellationToken: cancellationToken);

        return Ok(await MapEpicToResponse(epic, cancellationToken));
    }

    [HttpDelete("{epicId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteEpic(
        string projectId,
        string epicId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var epic = await FindEpicById(projectResult.Value!.Id, epicId, cancellationToken);
        if (epic is null)
        {
            return NotFound(new { message = "Epic not found." });
        }

        await _mongoDbContext.Epics.DeleteOneAsync(
            existingEpic => existingEpic.Id == epic.Id && existingEpic.ProjectId == epic.ProjectId,
            cancellationToken);

        var update = Builders<ProjectTask>.Update
            .Set(task => task.EpicId, null)
            .Set(task => task.UpdatedAt, DateTime.UtcNow);

        await _mongoDbContext.Tasks.UpdateManyAsync(
            task => task.ProjectId == epic.ProjectId && task.EpicId == epic.Id,
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

    private async Task<ProjectEpic?> FindEpicById(
        string projectId,
        string epicId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(epicId, out _))
        {
            return null;
        }

        return await _mongoDbContext.Epics
            .Find(epic => epic.Id == epicId && epic.ProjectId == projectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ActionResult<string?>> NormalizeAndValidateSprintId(
        string projectId,
        string? sprintId,
        CancellationToken cancellationToken)
    {
        var normalizedSprintId = NormalizeOptionalText(sprintId);
        if (normalizedSprintId is null)
        {
            return normalizedSprintId;
        }

        if (!ObjectId.TryParse(normalizedSprintId, out _))
        {
            ModelState.AddModelError(nameof(sprintId), "SprintId must be a valid sprint id.");
            return ValidationProblem(ModelState);
        }

        var sprintExists = await _mongoDbContext.Sprints
            .Find(sprint => sprint.Id == normalizedSprintId && sprint.ProjectId == projectId)
            .AnyAsync(cancellationToken);

        if (!sprintExists)
        {
            return NotFound(new { message = "Sprint not found." });
        }

        return normalizedSprintId;
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

    private async Task<List<EpicResponseDto>> MapEpicsToResponses(
        List<ProjectEpic> epics,
        CancellationToken cancellationToken)
    {
        var sprintsById = await LoadSprintsForEpics(epics, cancellationToken);
        return epics.Select(epic => MapEpicToResponse(epic, sprintsById)).ToList();
    }

    private async Task<EpicResponseDto> MapEpicToResponse(
        ProjectEpic epic,
        CancellationToken cancellationToken)
    {
        var sprintsById = await LoadSprintsForEpics(new List<ProjectEpic> { epic }, cancellationToken);
        return MapEpicToResponse(epic, sprintsById);
    }

    private static EpicResponseDto MapEpicToResponse(
        ProjectEpic epic,
        IReadOnlyDictionary<string, ProjectSprint> sprintsById)
    {
        var sprintName = epic.SprintId is not null && sprintsById.TryGetValue(epic.SprintId, out var sprint)
            ? sprint.Name
            : null;

        return new EpicResponseDto
        {
            Id = epic.Id,
            ProjectId = epic.ProjectId,
            SprintId = epic.SprintId,
            SprintName = sprintName,
            Name = epic.Name,
            Description = epic.Description,
            CreatedAt = epic.CreatedAt,
            UpdatedAt = epic.UpdatedAt
        };
    }

    private async Task<IReadOnlyDictionary<string, ProjectSprint>> LoadSprintsForEpics(
        List<ProjectEpic> epics,
        CancellationToken cancellationToken)
    {
        var sprintIds = epics
            .Select(epic => epic.SprintId)
            .Where(sprintId => !string.IsNullOrWhiteSpace(sprintId))
            .Distinct()
            .ToList();

        if (sprintIds.Count == 0)
        {
            return new Dictionary<string, ProjectSprint>();
        }

        var sprints = await _mongoDbContext.Sprints
            .Find(sprint => sprintIds.Contains(sprint.Id))
            .ToListAsync(cancellationToken);

        return sprints.ToDictionary(sprint => sprint.Id);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
