namespace MyFinances.Api.Categorization;

// Which side of the ledger a category belongs to: the categorize/entry pickers offer Income
// categories for positive amounts and Expense categories for negative ones.
public enum CategoryKind
{
    Expense = 0,
    Income = 1,
}

// A category a transaction can be assigned to (FR-007). Every category belongs to one user and
// can be created and deleted by them; a new user starts with a copy of DefaultCategories.
public class Category
{
    // Stable chart palette slot, so a category keeps its colour when others are added or deleted.
    public int ColorSlot { get; set; }

    public Guid Id { get; set; }
    public CategoryKind Kind { get; set; }
    public required string Name { get; set; }

    // Drives the order categories are returned in for the picker UI.
    public int SortOrder { get; set; }

    public Guid UserId { get; set; }
}