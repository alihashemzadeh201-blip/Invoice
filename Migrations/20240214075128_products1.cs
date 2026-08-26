using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class products1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Mu",
                table: "Products",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Mu",
                table: "Products",
                column: "Mu");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_MeasureUnits_Mu",
                table: "Products",
                column: "Mu",
                principalTable: "MeasureUnits",
                principalColumn: "Code");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_MeasureUnits_Mu",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Mu",
                table: "Products");

            migrationBuilder.AlterColumn<string>(
                name: "Mu",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
