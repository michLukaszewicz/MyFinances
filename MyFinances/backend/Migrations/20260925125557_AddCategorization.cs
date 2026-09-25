using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MyFinances.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCategorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInternalTransfer",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TransferFlagManuallySet",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "Groceries", 1 },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "Dining & Takeout", 2 },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "Transport", 3 },
                    { new Guid("00000000-0000-0000-0000-000000000004"), "Housing & Utilities", 4 },
                    { new Guid("00000000-0000-0000-0000-000000000005"), "Health", 5 },
                    { new Guid("00000000-0000-0000-0000-000000000006"), "Shopping", 6 },
                    { new Guid("00000000-0000-0000-0000-000000000007"), "Entertainment", 7 },
                    { new Guid("00000000-0000-0000-0000-000000000008"), "Travel", 8 },
                    { new Guid("00000000-0000-0000-0000-000000000009"), "Subscriptions", 9 },
                    { new Guid("00000000-0000-0000-0000-000000000010"), "Income", 10 },
                    { new Guid("00000000-0000-0000-0000-000000000011"), "Fees & Charges", 11 },
                    { new Guid("00000000-0000-0000-0000-000000000012"), "Other", 12 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_CategoryId",
                table: "Transactions",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Categories_CategoryId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsInternalTransfer",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransferFlagManuallySet",
                table: "Transactions");
        }
    }
}
