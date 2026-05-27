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
        [FromQuery] string? epicId,
        [FromQuery] string? sprintId,
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

        var normalizedEpicId = NormalizeOptionalText(epicId);
        if (normalizedEpicId is not null)
        {
            if (!ObjectId.TryParse(normalizedEpicId, out _))
            {
                ModelState.AddModelError(nameof(epicId), "EpicId must be a valid epic id.");
                return ValidationProblem(ModelState);
            }

            var epicExists = await _mongoDbContext.Epics
                .Find(epic => epic.Id == normalizedEpicId && epic.ProjectId == project.Id)
                .AnyAsync(cancellationToken);

            if (!epicExists)
            {
                return NotFound(new { message = "Epic not found." });
            }

            filter &= Builders<ProjectTask>.Filter.Eq(task => task.EpicId, normalizedEpicId);
        }

        var normalizedSprintId = NormalizeOptionalText(sprintId);
        if (normalizedSprintId is not null)
        {
            if (!ObjectId.TryParse(normalizedSprintId, out _))
            {
                ModelState.AddModelError(nameof(sprintId), "SprintId must be a valid sprint id.");
                return ValidationProblem(ModelState);
            }

            var sprintExists = await _mongoDbContext.Sprints
                .Find(sprint => sprint.Id == normalizedSprintId && sprint.ProjectId == project.Id)
                .AnyAsync(cancellationToken);

            if (!sprintExists)
            {
                return NotFound(new { message = "Sprint not found." });
            }

            var sprintEpicIds = await _mongoDbContext.Epics
                .Find(epic => epic.ProjectId == project.Id && epic.SprintId == normalizedSprintId)
                .Project(epic => epic.Id)
                .ToListAsync(cancellationToken);

            if (sprintEpicIds.Count == 0)
            {
                return Ok(new List<TaskResponseDto>());
            }

            filter &= Builders<ProjectTask>.Filter.In(task => task.EpicId, sprintEpicIds);
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

        if (!EnsureProjectMember(project, currentUserId))
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

        var epicValidation = await NormalizeAndValidateEpicId(project.Id, request.EpicId, cancellationToken);
        if (epicValidation.Error is not null)
        {
            return epicValidation.Error;
        }

        var task = new ProjectTask
        {
            ProjectId = project.Id,
            Title = request.Title.Trim(),
            Description = NormalizeOptionalText(request.Description),
            AssignedToUserId = assignedToUserId,
            EpicId = epicValidation.EpicId,
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

        if (!EnsureProjectMember(project, currentUserId))
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

        var epicValidation = await NormalizeAndValidateEpicId(project.Id, request.EpicId, cancellationToken);
        if (epicValidation.Error is not null)
        {
            return epicValidation.Error;
        }

        task.Title = request.Title.Trim();
        task.Description = NormalizeOptionalText(request.Description);
        task.AssignedToUserId = assignedToUserId;
        task.EpicId = epicValidation.EpicId;
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

        if (!EnsureProjectMember(project, currentUserId))
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

        await _mongoDbContext.TaskComments.DeleteManyAsync(
            comment => comment.ProjectId == project.Id && comment.TaskId == task.Id,
            cancellationToken);

        return NoContent();
    }

    [HttpGet("{taskId}/comments")]
    [ProducesResponseType<List<TaskCommentResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<TaskCommentResponseDto>>> GetTaskComments(
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

        var comments = await _mongoDbContext.TaskComments
            .Find(comment => comment.ProjectId == project.Id && comment.TaskId == task.Id)
            .SortBy(comment => comment.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(await MapCommentsToResponses(comments, cancellationToken));
    }

    [HttpPost("{taskId}/comments")]
    [ProducesResponseType<TaskCommentResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskCommentResponseDto>> CreateTaskComment(
        string projectId,
        string taskId,
        CreateTaskCommentRequestDto request,
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

        var body = NormalizeOptionalText(request.Body);
        if (body is null)
        {
            ModelState.AddModelError(nameof(request.Body), "Comment body is required.");
            return ValidationProblem(ModelState);
        }

        var comment = new TaskComment
        {
            ProjectId = project.Id,
            TaskId = task.Id,
            CreatedByUserId = currentUserId,
            Body = body,
            CreatedAt = DateTime.UtcNow
        };

        await _mongoDbContext.TaskComments.InsertOneAsync(comment, cancellationToken: cancellationToken);

        var response = await MapCommentToResponse(comment, cancellationToken);
        return CreatedAtAction(
            nameof(GetTaskComments),
            new { projectId = project.Id, taskId = task.Id },
            response);
    }

    [HttpDelete("{taskId}/comments/{commentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTaskComment(
        string projectId,
        string taskId,
        string commentId,
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

        var comment = await FindCommentById(project.Id, task.Id, commentId, cancellationToken);

        if (comment is null)
        {
            return NotFound(new { message = "Comment not found." });
        }

        await _mongoDbContext.TaskComments.DeleteOneAsync(
            existingComment => existingComment.Id == comment.Id && existingComment.TaskId == task.Id,
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

    private async Task<TaskComment?> FindCommentById(
        string projectId,
        string taskId,
        string commentId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(commentId, out _))
        {
            return null;
        }

        return await _mongoDbContext.TaskComments
            .Find(comment =>
                comment.Id == commentId &&
                comment.ProjectId == projectId &&
                comment.TaskId == taskId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool EnsureProjectMember(Project project, string userId)
    {
        return project.MemberIds.Contains(userId);
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

    private async Task<(string? EpicId, ActionResult? Error)> NormalizeAndValidateEpicId(
        string projectId,
        string? epicId,
        CancellationToken cancellationToken)
    {
        var normalizedEpicId = NormalizeOptionalText(epicId);
        if (normalizedEpicId is null)
        {
            return (null, null);
        }

        if (!ObjectId.TryParse(normalizedEpicId, out _))
        {
            ModelState.AddModelError(nameof(epicId), "EpicId must be a valid epic id.");
            return (null, ValidationProblem(ModelState));
        }

        var epicExists = await _mongoDbContext.Epics
            .Find(epic => epic.Id == normalizedEpicId && epic.ProjectId == projectId)
            .AnyAsync(cancellationToken);

        if (!epicExists)
        {
            return (null, NotFound(new { message = "Epic not found." }));
        }

        return (normalizedEpicId, null);
    }

    private async Task<List<TaskResponseDto>> MapTasksToResponses(
        List<ProjectTask> tasks,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForTasks(tasks, cancellationToken);
        var epicsById = await LoadEpicsForTasks(tasks, cancellationToken);
        var sprintsById = await LoadSprintsForEpics(epicsById.Values.ToList(), cancellationToken);
        var commentCounts = await LoadCommentCountsForTasks(tasks, cancellationToken);

        return tasks
            .Select(task => MapTaskToResponse(task, usersById, epicsById, sprintsById, commentCounts))
            .ToList();
    }

    private async Task<TaskResponseDto> MapTaskToResponse(
        ProjectTask task,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForTasks(new List<ProjectTask> { task }, cancellationToken);
        var epicsById = await LoadEpicsForTasks(new List<ProjectTask> { task }, cancellationToken);
        var sprintsById = await LoadSprintsForEpics(epicsById.Values.ToList(), cancellationToken);
        var commentCounts = await LoadCommentCountsForTasks(new List<ProjectTask> { task }, cancellationToken);

        return MapTaskToResponse(task, usersById, epicsById, sprintsById, commentCounts);
    }

    private static TaskResponseDto MapTaskToResponse(
        ProjectTask task,
        IReadOnlyDictionary<string, User> usersById,
        IReadOnlyDictionary<string, ProjectEpic> epicsById,
        IReadOnlyDictionary<string, ProjectSprint> sprintsById,
        IReadOnlyDictionary<string, int> commentCounts)
    {
        usersById.TryGetValue(task.CreatedByUserId, out var createdByUser);
        ProjectEpic? epic = null;
        ProjectSprint? sprint = null;

        if (!string.IsNullOrWhiteSpace(task.EpicId))
        {
            epicsById.TryGetValue(task.EpicId, out epic);
        }

        if (!string.IsNullOrWhiteSpace(epic?.SprintId))
        {
            sprintsById.TryGetValue(epic.SprintId, out sprint);
        }

        return new TaskResponseDto
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            AssignedToUser = MapOptionalUser(task.AssignedToUserId, usersById),
            CreatedByUser = MapRequiredUser(task.CreatedByUserId, createdByUser),
            EpicId = task.EpicId,
            EpicName = epic?.Name,
            SprintId = epic?.SprintId,
            SprintName = sprint?.Name,
            Status = task.Status,
            Priority = task.Priority,
            StartDate = task.StartDate,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            CommentCount = commentCounts.TryGetValue(task.Id, out var commentCount) ? commentCount : 0
        };
    }

    private async Task<IReadOnlyDictionary<string, int>> LoadCommentCountsForTasks(
        List<ProjectTask> tasks,
        CancellationToken cancellationToken)
    {
        var taskIds = tasks
            .Select(task => task.Id)
            .Where(taskId => !string.IsNullOrWhiteSpace(taskId))
            .Distinct()
            .ToList();

        if (taskIds.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var filter = Builders<TaskComment>.Filter.In(comment => comment.TaskId, taskIds);
        var counts = await _mongoDbContext.TaskComments
            .Aggregate()
            .Match(filter)
            .Group(
                comment => comment.TaskId,
                group => new
                {
                    TaskId = group.Key,
                    Count = group.Count()
                })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(item => item.TaskId, item => item.Count);
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

    private async Task<IReadOnlyDictionary<string, ProjectEpic>> LoadEpicsForTasks(
        List<ProjectTask> tasks,
        CancellationToken cancellationToken)
    {
        var epicIds = tasks
            .Select(task => task.EpicId)
            .Where(epicId => !string.IsNullOrWhiteSpace(epicId))
            .Distinct()
            .ToList();

        if (epicIds.Count == 0)
        {
            return new Dictionary<string, ProjectEpic>();
        }

        var epics = await _mongoDbContext.Epics
            .Find(epic => epicIds.Contains(epic.Id))
            .ToListAsync(cancellationToken);

        return epics.ToDictionary(epic => epic.Id);
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

    private async Task<List<TaskCommentResponseDto>> MapCommentsToResponses(
        List<TaskComment> comments,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForComments(comments, cancellationToken);
        return comments.Select(comment => MapCommentToResponse(comment, usersById)).ToList();
    }

    private async Task<TaskCommentResponseDto> MapCommentToResponse(
        TaskComment comment,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForComments(new List<TaskComment> { comment }, cancellationToken);
        return MapCommentToResponse(comment, usersById);
    }

    private static TaskCommentResponseDto MapCommentToResponse(
        TaskComment comment,
        IReadOnlyDictionary<string, User> usersById)
    {
        usersById.TryGetValue(comment.CreatedByUserId, out var createdByUser);

        return new TaskCommentResponseDto
        {
            Id = comment.Id,
            ProjectId = comment.ProjectId,
            TaskId = comment.TaskId,
            CreatedByUser = MapRequiredUser(comment.CreatedByUserId, createdByUser),
            Body = comment.Body,
            CreatedAt = comment.CreatedAt
        };
    }

    private async Task<IReadOnlyDictionary<string, User>> LoadUsersForComments(
        List<TaskComment> comments,
        CancellationToken cancellationToken)
    {
        var userIds = comments
            .Select(comment => comment.CreatedByUserId)
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
