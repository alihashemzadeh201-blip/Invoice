using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class currencycode1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cut",
                table: "InvoiceItems");

            migrationBuilder.AddColumn<int>(
                name: "CutId",
                table: "InvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_CutId",
                table: "InvoiceItems",
                column: "CutId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_CurrencyCodes_CutId",
                table: "InvoiceItems",
                column: "CutId",
                principalTable: "CurrencyCodes",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_CurrencyCodes_CutId",
                table: "InvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_CutId",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "CutId",
                table: "InvoiceItems");

            migrationBuilder.AddColumn<string>(
                name: "Cut",
                table: "InvoiceItems",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
