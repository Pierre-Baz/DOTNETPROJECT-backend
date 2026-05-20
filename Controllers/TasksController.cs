using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NetManage.Api.DTOs.Tasks;
using NetManage.Api.Models;
using NetManage.Api.Services;

namespace NetManage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId}/tasks")]
public class TasksController : ControllerBase
{
    private const string StatusTodo = "Todo";

    private static readonly Dictionary<string, string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Todo"] = "Todo",
        ["Started"] = "Started",
        ["Testing"] = "Testing",
        ["Finishing"] = "Finishing",
        ["Done"] = "Done"
    };

    private static readonly Dictionary<string, string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Low"] = "Low",
        ["Medium"] = "Medium",
        ["High"] = "High",
        ["Critical"] = "Critical"
    };

    private readonly MongoDbContext _mongoDbContext;

    public TasksController(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    [HttpGet]
    [ProducesResponseType<List<TaskResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<TaskResponseDto>>> GetProjectTasks(
        string projectId,
        [FromQuery] string? status,
        [FromQuery] string? assignedToUserId,
        [FromQuery] string? priority,
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

        if (!EnsureProjectMember(project, currentUserId))
        {
            return Forbid();
        }

        var filter = Builders<ProjectTask>.Filter.Eq(task => task.ProjectId, project.Id);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryNormalizeStatus(status, out var normalizedStatus))
            {
                ModelState.AddModelError(nameof(status), "Status must be one of: Todo, Started, Testing, Finishing, Done.");
                return ValidationProblem(ModelState);
            }

            filter &= Builders<ProjectTask>.Filter.Eq(task => task.Status, normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!TryNormalizePriority(priority, out var normalizedPriority))
            {
                ModelState.AddModelError(nameof(priority), "Priority must be one of: Low, Medium, High, Critical.");
                return ValidationProblem(ModelState);
            }

            filter &= Builders<ProjectTask>.Filter.Eq(task => task.Priority, normalizedPriority);
        }

        var normalizedAssignedToUserId = NormalizeOptionalText(assignedToUserId);
        if (normalizedAssignedToUserId is not null)
        {
            if (!ObjectId.TryParse(normalizedAssignedToUserId, out _))
            {
                ModelState.AddModelError(nameof(assignedToUserId), "AssignedToUserId must be a valid user id.");
                return ValidationProblem(ModelState);
            }

            filter &= Builders<ProjectTask>.Filter.Eq(task => task.AssignedToUserId, normalizedAssignedToUserId);
        }

        var tasks = await _mongoDbContext.Tasks
            .Find(filter)
            .SortByDescending(task => task.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(await MapTasksToResponses(tasks, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<TaskResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponseDto>> CreateTask(
        string projectId,
        CreateTaskRequestDto request,
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

        if (!EnsureProjectOwner(project, currentUserId))
        {
            return Forbid();
        }

        if (!ValidateTaskDetails(request.Title, request.Priority, request.StartDate, request.DueDate, request.AssignedToUserId, out var normalizedPriority, out var assignedToUserId))
        {
            return ValidationProblem(ModelState);
        }

        var assignmentError = await ValidateAssignedUser(project, assignedToUserId, cancellationToken);
        if (assignmentError is not null)
        {
            return assignmentError;
        }

        var task = new ProjectTask
        {
            ProjectId = project.Id,
            Title = request.Title.Trim(),
            Description = NormalizeOptionalText(request.Description),
            AssignedToUserId = assignedToUserId,
            CreatedByUserId = currentUserId,
            Status = StatusTodo,
            Priority = normalizedPriority,
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            CreatedAt = DateTime.UtcNow
        };

        await _mongoDbContext.Tasks.InsertOneAsync(task, cancellationToken: cancellationToken);

        var response = await MapTaskToResponse(task, cancellationToken);
        return CreatedAtAction(nameof(GetTask), new { projectId = project.Id, taskId = task.Id }, response);
    }

    [HttpGet("{taskId}")]
    [ProducesResponseType<TaskResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponseDto>> GetTask(
        string projectId,
        string taskId,
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

        if (!EnsureProjectMember(project, currentUserId))
        {
            return Forbid();
        }

        var task = await FindTaskById(project.Id, taskId, cancellationToken);

        if (task is null)
        {
            return NotFound(new { message = "Task not found." });
        }

        return Ok(await MapTaskToResponse(task, cancellationToken));
    }

    [HttpPut("{taskId}")]
    [ProducesResponseType<TaskResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponseDto>> UpdateTask(
        string projectId,
        string taskId,
        UpdateTaskRequestDto request,
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

        if (!EnsureProjectOwner(project, currentUserId))
        {
            return Forbid();
        }

        var task = await FindTaskById(project.Id, taskId, cancellationToken);

        if (task is null)
        {
            return NotFound(new { message = "Task not found." });
        }

        if (!ValidateTaskDetails(request.Title, request.Priority, request.StartDate, request.DueDate, request.AssignedToUserId, out var normalizedPriority, out var assignedToUserId))
        {
            return ValidationProblem(ModelState);
        }

        var assignmentError = await ValidateAssignedUser(project, assignedToUserId, cancellationToken);
        if (assignmentError is not null)
        {
            return assignmentError;
        }

        task.Title = request.Title.Trim();
        task.Description = NormalizeOptionalText(request.Description);
        task.AssignedToUserId = assignedToUserId;
        task.Priority = normalizedPriority;
        task.StartDate = request.StartDate;
        task.DueDate = request.DueDate;
        task.UpdatedAt = DateTime.UtcNow;

        await _mongoDbContext.Tasks.ReplaceOneAsync(
            existingTask => existingTask.Id == task.Id && existingTask.ProjectId == project.Id,
            task,
            cancellationToken: cancellationToken);

        return Ok(await MapTaskToResponse(task, cancellationToken));
    }

    [HttpPatch("{taskId}/status")]
    [ProducesResponseType<TaskResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponseDto>> UpdateTaskStatus(
        string projectId,
        string taskId,
        UpdateTaskStatusRequestDto request,
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

        if (!EnsureProjectMember(project, currentUserId))
        {
            return Forbid();
        }

        var task = await FindTaskById(project.Id, taskId, cancellationToken);

        if (task is null)
        {
            return NotFound(new { message = "Task not found." });
        }

        if (!CanUpdateTaskStatus(project, task, currentUserId))
        {
            return Forbid();
        }

        if (!TryNormalizeStatus(request.Status, out var normalizedStatus))
        {
            ModelState.AddModelError(nameof(request.Status), "Status must be one of: Todo, Started, Testing, Finishing, Done.");
            return ValidationProblem(ModelState);
        }

        task.Status = normalizedStatus;
        task.UpdatedAt = DateTime.UtcNow;

        var update = Builders<ProjectTask>.Update
            .Set(existingTask => existingTask.Status, task.Status)
            .Set(existingTask => existingTask.UpdatedAt, task.UpdatedAt);

        await _mongoDbContext.Tasks.UpdateOneAsync(
            existingTask => existingTask.Id == task.Id && existingTask.ProjectId == project.Id,
            update,
            cancellationToken: cancellationToken);

        return Ok(await MapTaskToResponse(task, cancellationToken));
    }

    [HttpDelete("{taskId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTask(
        string projectId,
        string taskId,
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

        if (!EnsureProjectOwner(project, currentUserId))
        {
            return Forbid();
        }

        var task = await FindTaskById(project.Id, taskId, cancellationToken);

        if (task is null)
        {
            return NotFound(new { message = "Task not found." });
        }

        await _mongoDbContext.Tasks.DeleteOneAsync(
            existingTask => existingTask.Id == task.Id && existingTask.ProjectId == project.Id,
            cancellationToken);

        return NoContent();
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

    private async Task<ProjectTask?> FindTaskById(
        string projectId,
        string taskId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(taskId, out _))
        {
            return null;
        }

        return await _mongoDbContext.Tasks
            .Find(task => task.Id == taskId && task.ProjectId == projectId)
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

    private static bool CanUpdateTaskStatus(Project project, ProjectTask task, string userId)
    {
        return project.OwnerId == userId || task.AssignedToUserId == userId;
    }

    private bool ValidateTaskDetails(
        string title,
        string priority,
        DateTime? startDate,
        DateTime? dueDate,
        string? assignedToUserId,
        out string normalizedPriority,
        out string? normalizedAssignedToUserId)
    {
        normalizedPriority = string.Empty;
        normalizedAssignedToUserId = NormalizeOptionalText(assignedToUserId);

        if (string.IsNullOrWhiteSpace(title))
        {
            ModelState.AddModelError(nameof(title), "Title is required.");
        }

        if (!TryNormalizePriority(priority, out normalizedPriority))
        {
            ModelState.AddModelError(nameof(priority), "Priority must be one of: Low, Medium, High, Critical.");
        }

        if (startDate.HasValue && dueDate.HasValue && dueDate.Value < startDate.Value)
        {
            ModelState.AddModelError(nameof(dueDate), "DueDate cannot be before StartDate.");
        }

        if (normalizedAssignedToUserId is not null && !ObjectId.TryParse(normalizedAssignedToUserId, out _))
        {
            ModelState.AddModelError(nameof(assignedToUserId), "AssignedToUserId must be a valid user id.");
        }

        return ModelState.IsValid;
    }

    private async Task<ActionResult?> ValidateAssignedUser(
        Project project,
        string? assignedToUserId,
        CancellationToken cancellationToken)
    {
        if (assignedToUserId is null)
        {
            return null;
        }

        var user = await FindUserById(assignedToUserId, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "Assigned user not found." });
        }

        if (!project.MemberIds.Contains(user.Id))
        {
            return BadRequest(new { message = "Assigned user must be a project member." });
        }

        return null;
    }

    private async Task<List<TaskResponseDto>> MapTasksToResponses(
        List<ProjectTask> tasks,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForTasks(tasks, cancellationToken);
        return tasks.Select(task => MapTaskToResponse(task, usersById)).ToList();
    }

    private async Task<TaskResponseDto> MapTaskToResponse(
        ProjectTask task,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForTasks(new List<ProjectTask> { task }, cancellationToken);
        return MapTaskToResponse(task, usersById);
    }

    private static TaskResponseDto MapTaskToResponse(
        ProjectTask task,
        IReadOnlyDictionary<string, User> usersById)
    {
        usersById.TryGetValue(task.CreatedByUserId, out var createdByUser);

        return new TaskResponseDto
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            AssignedToUser = MapOptionalUser(task.AssignedToUserId, usersById),
            CreatedByUser = MapRequiredUser(task.CreatedByUserId, createdByUser),
            Status = task.Status,
            Priority = task.Priority,
            StartDate = task.StartDate,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }

    private async Task<IReadOnlyDictionary<string, User>> LoadUsersForTasks(
        List<ProjectTask> tasks,
        CancellationToken cancellationToken)
    {
        var userIds = tasks
            .SelectMany(task => new[] { task.AssignedToUserId, task.CreatedByUserId })
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

    private static TaskAssigneeDto? MapOptionalUser(
        string? userId,
        IReadOnlyDictionary<string, User> usersById)
    {
        if (string.IsNullOrWhiteSpace(userId) || !usersById.TryGetValue(userId, out var user))
        {
            return null;
        }

        return MapUser(user);
    }

    private static TaskAssigneeDto MapRequiredUser(string userId, User? user)
    {
        if (user is null)
        {
            return new TaskAssigneeDto { Id = userId };
        }

        return MapUser(user);
    }

    private static TaskAssigneeDto MapUser(User user)
    {
        return new TaskAssigneeDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email
        };
    }

    private static bool TryNormalizeStatus(string? value, out string normalizedStatus)
    {
        return TryNormalizeAllowedValue(value, AllowedStatuses, out normalizedStatus);
    }

    private static bool TryNormalizePriority(string? value, out string normalizedPriority)
    {
        return TryNormalizeAllowedValue(value, AllowedPriorities, out normalizedPriority);
    }

    private static bool TryNormalizeAllowedValue(
        string? value,
        IReadOnlyDictionary<string, string> allowedValues,
        out string normalizedValue)
    {
        normalizedValue = string.Empty;
        var trimmedValue = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            return false;
        }

        if (!allowedValues.TryGetValue(trimmedValue, out var matchedValue))
        {
            return false;
        }

        normalizedValue = matchedValue;
        return true;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
