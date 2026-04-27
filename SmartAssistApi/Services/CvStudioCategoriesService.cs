using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;

namespace SmartAssistApi.Services;

public sealed class CvStudioCategoriesService(SmartAssistDbContext db)
{
    public async Task<(IReadOnlyList<CvUserCategoryEntity> categories, Dictionary<Guid, Guid> assignments)>
        GetAllAsync(string clerkUserId, CancellationToken ct = default)
    {
        var categories = await db.CvUserCategories
            .AsNoTracking()
            .Where(x => x.ClerkUserId == clerkUserId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        var assignments = await db.CvResumeCategoryAssignments
            .AsNoTracking()
            .Where(x => x.ClerkUserId == clerkUserId)
            .ToListAsync(ct);

        var assignmentMap = assignments.ToDictionary(x => x.ResumeId, x => x.CategoryId);
        return (categories, assignmentMap);
    }

    public async Task<CvUserCategoryEntity> CreateAsync(string clerkUserId, string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 80)
            throw new ArgumentException("Name ist leer oder zu lang.", nameof(name));

        var maxOrder = await db.CvUserCategories
            .Where(x => x.ClerkUserId == clerkUserId)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(ct) ?? -1;

        var entity = new CvUserCategoryEntity
        {
            Id = Guid.NewGuid(),
            ClerkUserId = clerkUserId,
            Name = trimmed,
            SortOrder = maxOrder + 1,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.CvUserCategories.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> DeleteAsync(string clerkUserId, Guid categoryId, CancellationToken ct = default)
    {
        var entity = await db.CvUserCategories
            .FirstOrDefaultAsync(x => x.Id == categoryId && x.ClerkUserId == clerkUserId, ct);
        if (entity is null) return false;

        db.CvUserCategories.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task AssignAsync(string clerkUserId, Guid resumeId, Guid? categoryId, CancellationToken ct = default)
    {
        var existing = await db.CvResumeCategoryAssignments
            .FirstOrDefaultAsync(x => x.ResumeId == resumeId && x.ClerkUserId == clerkUserId, ct);

        if (categoryId is null)
        {
            if (existing is not null)
            {
                db.CvResumeCategoryAssignments.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        // Verify category belongs to the same user
        var catExists = await db.CvUserCategories
            .AnyAsync(x => x.Id == categoryId && x.ClerkUserId == clerkUserId, ct);
        if (!catExists) throw new ArgumentException("Kategorie nicht gefunden.", nameof(categoryId));

        if (existing is null)
        {
            db.CvResumeCategoryAssignments.Add(new CvResumeCategoryAssignmentEntity
            {
                ResumeId = resumeId,
                ClerkUserId = clerkUserId,
                CategoryId = categoryId.Value,
            });
        }
        else
        {
            existing.CategoryId = categoryId.Value;
        }

        await db.SaveChangesAsync(ct);
    }
}
