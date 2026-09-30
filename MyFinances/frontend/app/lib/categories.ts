// Mirrors the backend's CategorizationContracts.cs CategoryDto.
export interface CategoryDto {
  id: string;
  name: string;
  kind: "expense" | "income";
}

// Categories a transaction of this amount can be assigned to: income categories for positive
// amounts, expense categories for negative ones. A zero/unknown amount gets the full list. The
// currently-assigned category is always kept so legacy mismatches still render as selected.
export function categoriesForAmount(
  categories: CategoryDto[],
  amount: number | null,
  currentCategoryId?: string | null,
): CategoryDto[] {
  if (amount === null || Number.isNaN(amount) || amount === 0) return categories;
  const kind = amount > 0 ? "income" : "expense";
  return categories.filter((c) => c.kind === kind || c.id === currentCategoryId);
}
