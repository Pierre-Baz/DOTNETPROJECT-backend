using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NetManage.Api.DTOs.Wiki;
using NetManage.Api.Models;
using NetManage.Api.Services;

namespace NetManage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId}/wiki")]
public class WikiController : ControllerBase
{
    private readonly MongoDbContext _mongoDbContext;

    public WikiController(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    [HttpGet]
    [ProducesResponseType<List<WikiPageResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<WikiPageResponseDto>>> GetWikiPages(
        string projectId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var pages = await _mongoDbContext.WikiPages
            .Find(page => page.ProjectId == projectResult.Value!.Id)
            .SortByDescending(page => page.UpdatedAt)
            .ThenByDescending(page => page.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(await MapWikiPagesToResponses(pages, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<WikiPageResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiPageResponseDto>> CreateWikiPage(
        string projectId,
        CreateWikiPageRequestDto request,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        if (!ValidateRequiredText(request.Title, nameof(request.Title)))
        {
            return ValidationProblem(ModelState);
        }

        var page = new WikiPage
        {
            ProjectId = projectResult.Value!.Id,
            Title = request.Title.Trim(),
            Content = request.Content?.Trim() ?? string.Empty,
            CreatedByUserId = currentUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _mongoDbContext.WikiPages.InsertOneAsync(page, cancellationToken: cancellationToken);

        var response = await MapWikiPageToResponse(page, cancellationToken);
        return CreatedAtAction(nameof(GetWikiPage), new { projectId = page.ProjectId, pageId = page.Id }, response);
    }

    [HttpGet("{pageId}")]
    [ProducesResponseType<WikiPageResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiPageResponseDto>> GetWikiPage(
        string projectId,
        string pageId,
        CancellationToken cancellationToken)
    {
        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var page = await FindWikiPageById(projectResult.Value!.Id, pageId, cancellationToken);
        if (page is null)
        {
            return NotFound(new { message = "Wiki page not found." });
        }

        return Ok(await MapWikiPageToResponse(page, cancellationToken));
    }

    [HttpPut("{pageId}")]
    [ProducesResponseType<WikiPageResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiPageResponseDto>> UpdateWikiPage(
        string projectId,
        string pageId,
        UpdateWikiPageRequestDto request,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var page = await FindWikiPageById(projectResult.Value!.Id, pageId, cancellationToken);
        if (page is null)
        {
            return NotFound(new { message = "Wiki page not found." });
        }

        if (!ValidateRequiredText(request.Title, nameof(request.Title)))
        {
            return ValidationProblem(ModelState);
        }

        page.Title = request.Title.Trim();
        page.Content = request.Content?.Trim() ?? string.Empty;
        page.UpdatedByUserId = currentUserId;
        page.UpdatedAt = DateTime.UtcNow;

        await _mongoDbContext.WikiPages.ReplaceOneAsync(
            existingPage => existingPage.Id == page.Id && existingPage.ProjectId == page.ProjectId,
            page,
            cancellationToken: cancellationToken);

        return Ok(await MapWikiPageToResponse(page, cancellationToken));
    }

    [HttpDelete("{pageId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWikiPage(
        string projectId,
        string pageId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Authentication token is invalid." });
        }

        var projectResult = await LoadProjectForMember(projectId, cancellationToken);
        if (projectResult.Result is not null)
        {
            return projectResult.Result;
        }

        var project = projectResult.Value!;
        var page = await FindWikiPageById(project.Id, pageId, cancellationToken);
        if (page is null)
        {
            return NotFound(new { message = "Wiki page not found." });
        }

        await _mongoDbContext.WikiPages.DeleteOneAsync(
            existingPage => existingPage.Id == page.Id && existingPage.ProjectId == project.Id,
            cancellationToken);

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

    private async Task<WikiPage?> FindWikiPageById(
        string projectId,
        string pageId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(pageId, out _))
        {
            return null;
        }

        return await _mongoDbContext.WikiPages
            .Find(page => page.Id == pageId && page.ProjectId == projectId)
            .FirstOrDefaultAsync(cancellationToken);
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

    private async Task<List<WikiPageResponseDto>> MapWikiPagesToResponses(
        List<WikiPage> pages,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForPages(pages, cancellationToken);
        return pages.Select(page => MapWikiPageToResponse(page, usersById)).ToList();
    }

    private async Task<WikiPageResponseDto> MapWikiPageToResponse(
        WikiPage page,
        CancellationToken cancellationToken)
    {
        var usersById = await LoadUsersForPages(new List<WikiPage> { page }, cancellationToken);
        return MapWikiPageToResponse(page, usersById);
    }

    private static WikiPageResponseDto MapWikiPageToResponse(
        WikiPage page,
        IReadOnlyDictionary<string, User> usersById)
    {
        usersById.TryGetValue(page.CreatedByUserId, out var createdByUser);

        return new WikiPageResponseDto
        {
            Id = page.Id,
            ProjectId = page.ProjectId,
            Title = page.Title,
            Content = page.Content,
            CreatedByUserId = page.CreatedByUserId,
            CreatedByName = createdByUser?.FullName ?? string.Empty,
            CreatedAt = page.CreatedAt,
            UpdatedAt = page.UpdatedAt
        };
    }

    private async Task<IReadOnlyDictionary<string, User>> LoadUsersForPages(
        List<WikiPage> pages,
        CancellationToken cancellationToken)
    {
        var userIds = pages
            .Select(page => page.CreatedByUserId)
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
}
