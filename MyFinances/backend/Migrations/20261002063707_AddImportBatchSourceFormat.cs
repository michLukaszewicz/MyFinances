using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyFinances.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchSourceFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceFormat",
                table: "ImportBatches",
                type: "text",
                nullable: false,
                defaultValue: "Csv");

            // Backfill: VeloBank only ever had a PDF parser, so its batches came from PDFs.
            // Every other existing batch stays "Csv" (the column default).
            migrationBuilder.Sql(
                """
                UPDATE "ImportBatches" ib
                SET "SourceFormat" = 'Pdf'
                FROM "Accounts" a
                WHERE ib."AccountId" = a."Id" AND a."Bank" = 'VeloBank';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceFormat",
                table: "ImportBatches");
        }
    }
}
