using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MyFinances.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerUserCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColorSlot",
                table: "Categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Every existing user gets their own copy of the shared starter categories, their
            // transactions are re-pointed to those copies, and only then are the shared rows deleted below.
            migrationBuilder.Sql(@"
                INSERT INTO ""Categories"" (""Id"", ""Name"", ""SortOrder"", ""Kind"", ""UserId"", ""ColorSlot"")
                SELECT gen_random_uuid(), c.""Name"", c.""SortOrder"", c.""Kind"", u.""Id"", c.""SortOrder"" - 1
                FROM ""AspNetUsers"" u
                CROSS JOIN ""Categories"" c
                WHERE c.""UserId"" IS NULL;");

            migrationBuilder.Sql(@"
                UPDATE ""Transactions"" t
                SET ""CategoryId"" = usercat.""Id""
                FROM ""Categories"" shared, ""Categories"" usercat
                WHERE t.""CategoryId"" = shared.""Id""
                    AND shared.""UserId"" IS NULL
                    AND usercat.""UserId"" = t.""UserId""
                    AND usercat.""Name"" = shared.""Name"";");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000014"));

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorSlot",
                table: "Categories");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Categories",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Kind", "Name", "SortOrder", "UserId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), 0, "Groceries", 1, null },
                    { new Guid("00000000-0000-0000-0000-000000000002"), 0, "Dining & Takeout", 2, null },
                    { new Guid("00000000-0000-0000-0000-000000000003"), 0, "Transport", 3, null },
                    { new Guid("00000000-0000-0000-0000-000000000004"), 0, "Housing & Utilities", 4, null },
                    { new Guid("00000000-0000-0000-0000-000000000005"), 0, "Health", 5, null },
                    { new Guid("00000000-0000-0000-0000-000000000006"), 0, "Shopping", 6, null },
                    { new Guid("00000000-0000-0000-0000-000000000007"), 0, "Entertainment", 7, null },
                    { new Guid("00000000-0000-0000-0000-000000000008"), 0, "Travel", 8, null },
                    { new Guid("00000000-0000-0000-0000-000000000009"), 0, "Subscriptions", 9, null },
                    { new Guid("00000000-0000-0000-0000-000000000010"), 1, "Income", 10, null },
                    { new Guid("00000000-0000-0000-0000-000000000011"), 0, "Fees & Charges", 11, null },
                    { new Guid("00000000-0000-0000-0000-000000000012"), 0, "Other", 12, null },
                    { new Guid("00000000-0000-0000-0000-000000000013"), 1, "Refunds & Reimbursements", 13, null },
                    { new Guid("00000000-0000-0000-0000-000000000014"), 1, "Other income", 14, null }
                });
        }
    }
}
