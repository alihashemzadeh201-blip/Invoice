using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class creator : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Creator",
                table: "Profiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "Invoices",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ProfileId",
                table: "Invoices",
                column: "ProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Profiles_ProfileId",
                table: "Invoices",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Profiles_ProfileId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ProfileId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Creator",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "Invoices");
        }
    }
}
