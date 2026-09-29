namespace MyFinances.Api.Dashboard;

// Amount is the positive total spent in the category for the current month.
public record CategorySpendDto(Guid CategoryId, string CategoryName, decimal Amount);
