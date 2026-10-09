namespace MyFinances.Api.Categorization;

// The starter set every new user gets; afterwards they are ordinary categories the user may delete.
public static class DefaultCategories
{
    private static readonly (string Name, CategoryKind Kind)[] Starter =
    [
        ("Groceries", CategoryKind.Expense),
        ("Dining & Takeout", CategoryKind.Expense),
        ("Transport", CategoryKind.Expense),
        ("Housing & Utilities", CategoryKind.Expense),
        ("Health", CategoryKind.Expense),
        ("Shopping", CategoryKind.Expense),
        ("Entertainment", CategoryKind.Expense),
        ("Travel", CategoryKind.Expense),
        ("Subscriptions", CategoryKind.Expense),
        ("Income", CategoryKind.Income),
        ("Fees & Charges", CategoryKind.Expense),
        ("Other", CategoryKind.Expense),
        ("Refunds & Reimbursements", CategoryKind.Income),
        ("Other income", CategoryKind.Income),
    ];

    public static IEnumerable<Category> CreateFor(Guid userId) =>
        Starter.Select((entry, index) => new Category
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = entry.Name,
            Kind = entry.Kind,
            SortOrder = index + 1,
            ColorSlot = index,
        });
}
