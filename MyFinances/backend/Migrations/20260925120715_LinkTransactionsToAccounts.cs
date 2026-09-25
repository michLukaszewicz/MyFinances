using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyFinances.Api.Migrations
{
    /// <inheritdoc />
    public partial class LinkTransactionsToAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-launch dev data only: AccountId is becoming non-nullable and no valid value
            // exists for existing rows, so they're wiped rather than backfilled (user decision).
            // ImportBatches must go first since Transactions references it via FK.
            migrationBuilder.Sql("DELETE FROM \"Transactions\";");
            migrationBuilder.Sql("DELETE FROM \"ImportBatches\";");

            migrationBuilder.DropColumn(
                name: "Bank",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Bank",
                table: "ImportBatches");

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "Transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "ImportBatches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_AccountId",
                table: "ImportBatches",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_Accounts_AccountId",
                table: "ImportBatches",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportBatches_Accounts_AccountId",
                table: "ImportBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_AccountId",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "ImportBatches");

            migrationBuilder.AddColumn<string>(
                name: "Bank",
                table: "Transactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Bank",
                table: "ImportBatches",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
