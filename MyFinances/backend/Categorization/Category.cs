namespace MyFinances.Api.Categorization;

// A fixed, seeded category a transaction can be assigned to (FR-007). Not user-owned or
// user-editable in this slice — no category-management FR exists yet.
public class Category
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // Drives the order categories are returned in for the picker UI.
    public int SortOrder { get; set; }
}
