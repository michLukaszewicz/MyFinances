using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Categorization;

namespace MyFinances.Api.Tests.Support;

// Dashboard tests refer to categories by fixed ids (the last byte doubles as the sort order); a
// category is created for the owning user the first time an id is seeded.
internal static class FixedCategories
{
    public static async Task EnsureAsync(AppDbContext db, Guid userId, Guid? categoryId)
    {
        if (categoryId is null || await db.Categories.AnyAsync(c => c.Id == categoryId))
        {
            return;
        }
        var sortOrder = categoryId.Value.ToByteArray()[15];
        db.Categories.Add(new Category
        {
            Id = categoryId.Value,
            UserId = userId,
            Name = $"Fixed category {sortOrder}",
            SortOrder = sortOrder,
            ColorSlot = sortOrder,
        });
        await db.SaveChangesAsync();
    }
}
