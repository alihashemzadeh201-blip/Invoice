using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class somechanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_CurrencyCodes_CutId",
                table: "InvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_MeasureUnits_MuId",
                table: "InvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_CutId",
                table: "InvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_MuId",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "CutId",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "MuId",
                table: "InvoiceItems");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "MeasureUnits",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Cut",
                table: "InvoiceItems",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mu",
                table: "InvoiceItems",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AlphabetCode",
                table: "CurrencyCodes",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MeasureUnits_Code",
                table: "MeasureUnits",
                column: "Code");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CurrencyCodes_AlphabetCode",
                table: "CurrencyCodes",
                column: "AlphabetCode");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_Cut",
                table: "InvoiceItems",
                column: "Cut");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_Mu",
                table: "InvoiceItems",
                column: "Mu");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_CurrencyCodes_Cut",
                table: "InvoiceItems",
                column: "Cut",
                principalTable: "CurrencyCodes",
                principalColumn: "AlphabetCode");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_MeasureUnits_Mu",
                table: "InvoiceItems",
                column: "Mu",
                principalTable: "MeasureUnits",
                principalColumn: "Code");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_CurrencyCodes_Cut",
                table: "InvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_MeasureUnits_Mu",
                table: "InvoiceItems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MeasureUnits_Code",
                table: "MeasureUnits");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_Cut",
                table: "InvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_Mu",
                table: "InvoiceItems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CurrencyCodes_AlphabetCode",
                table: "CurrencyCodes");

            migrationBuilder.DropColumn(
                name: "Cut",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Mu",
                table: "InvoiceItems");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "MeasureUnits",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<int>(
                name: "CutId",
                table: "InvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MuId",
                table: "InvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AlphabetCode",
                table: "CurrencyCodes",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_CutId",
                table: "InvoiceItems",
                column: "CutId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_MuId",
                table: "InvoiceItems",
                column: "MuId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_CurrencyCodes_CutId",
                table: "InvoiceItems",
                column: "CutId",
                principalTable: "CurrencyCodes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_MeasureUnits_MuId",
                table: "InvoiceItems",
                column: "MuId",
                principalTable: "MeasureUnits",
                principalColumn: "Id");
        }
    }
}
