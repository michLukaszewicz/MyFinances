using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyFinances.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bank",
                table: "Accounts",
                type: "text",
                nullable: false,
                defaultValue: "Other");

            // Backfill: accounts whose old free-text name is (case-insensitively) a bank that
            // had a parser keep that bank, so their import mismatch check carries on working.
            // Everything else stays "Other" (never checked).
            migrationBuilder.Sql(
                """
                UPDATE "Accounts" a
                SET "Bank" = b.name
                FROM (VALUES ('mBank'), ('Erste'), ('VeloBank')) AS b(name)
                WHERE lower(a."BankName") = lower(b.name);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bank",
                table: "Accounts");
        }
    }
}
