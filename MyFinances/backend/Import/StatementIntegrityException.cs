namespace MyFinances.Api.Import;

// Thrown by a parser that recognised a statement but cannot trust what it read (e.g. a PDF whose
// rows fail the balance check). The message is shown to the user as-is by ImportEndpoints (HTTP
// 422), so it may name the failing check, the page and a transaction date, but must never contain
// amounts, descriptions or names.
public class StatementIntegrityException(string message) : Exception(message);
