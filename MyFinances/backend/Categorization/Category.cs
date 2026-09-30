namespace MyFinances.Api.Categorization;

// A fixed, seeded category a transaction can be assigned to (FR-007). Not user-owned or
// user-editable in this slice — no category-management FR exists yet.
// Which side of the ledger a category belongs to: the categorize/entry pickers offer Income
// categories for positive amounts and Expense categories for negative ones.
public enum CategoryKind
{
    Expense = 0,
    Income = 1,
}

public class Category
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // Drives the order categories are returned in for the picker UI.
    public int SortOrder { get; set; }

    public CategoryKind Kind { get; set; }
}
