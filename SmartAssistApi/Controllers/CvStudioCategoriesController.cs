using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/cv-studio/categories")]
public sealed class CvStudioCategoriesController(
    ClerkAuthService clerkAuth,
    CvStudioCategoriesService categoriesService) : ControllerBase
{
    private (string userId, IActionResult? unauthorized) RequireUser()
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return (string.Empty, Unauthorized(new { error = "auth_required" }));
        return (userId, null);
    }

    /// <summary>Returns the user's categories and resume-to-category assignment map.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CvCategoriesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;

        var (categories, assignments) = await categoriesService.GetAllAsync(uid, cancellationToken);

        return Ok(new CvCategoriesResponse
        {
            Categories = categories.Select(c => new CvCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                SortOrder = c.SortOrder,
            }).ToList(),
            Assignments = assignments.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => kvp.Value.ToString()),
        });
    }

    [HttpPost]
    [ProducesResponseType(typeof(CvCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateCvCategoryRequest request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name darf nicht leer sein." });

        var entity = await categoriesService.CreateAsync(uid, request.Name, cancellationToken);
        var dto = new CvCategoryDto { Id = entity.Id, Name = entity.Name, SortOrder = entity.SortOrder };
        return CreatedAtAction(nameof(Get), dto);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;

        var ok = await categoriesService.DeleteAsync(uid, id, cancellationToken);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Assigns or removes a resume from a category. Body: { categoryId: "uuid" | null }</summary>
    [HttpPut("assignments/{resumeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Assign(Guid resumeId, [FromBody] AssignCategoryRequest request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;

        Guid? catId = null;
        if (!string.IsNullOrEmpty(request.CategoryId))
        {
            if (!Guid.TryParse(request.CategoryId, out var parsed))
                return BadRequest(new { error = "Ungültige Kategorie-ID." });
            catId = parsed;
        }

        try
        {
            await categoriesService.AssignAsync(uid, resumeId, catId, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return NoContent();
    }
}

public sealed class CvCategoriesResponse
{
    public List<CvCategoryDto> Categories { get; set; } = [];
    /// <summary>resumeId (string) → categoryId (string)</summary>
    public Dictionary<string, string> Assignments { get; set; } = [];
}

public sealed class CvCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class CreateCvCategoryRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class AssignCategoryRequest
{
    public string? CategoryId { get; set; }
}
