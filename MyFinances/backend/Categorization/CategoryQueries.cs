namespace MyFinances.Api.Categorization;

public static class CategoryQueries
{
    public static IQueryable<Category> OwnedBy(this IQueryable<Category> categories, Guid userId) =>
        categories.Where(c => c.UserId == userId);
}
