using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class debts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerDebts_Users_CustomerUsername",
                table: "CustomerDebts");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPayments_Users_CustomerUsername",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_CustomerUsername",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDebts_CustomerUsername",
                table: "CustomerDebts");

            migrationBuilder.DropColumn(
                name: "CustomerUsername",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "CustomerUsername",
                table: "CustomerDebts");

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "CustomerPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "CustomerDebts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_ProfileId",
                table: "CustomerPayments",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebts_ProfileId",
                table: "CustomerDebts",
                column: "ProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDebts_Profiles_ProfileId",
                table: "CustomerDebts",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPayments_Profiles_ProfileId",
                table: "CustomerPayments",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerDebts_Profiles_ProfileId",
                table: "CustomerDebts");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPayments_Profiles_ProfileId",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_ProfileId",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDebts_ProfileId",
                table: "CustomerDebts");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "CustomerDebts");

            migrationBuilder.AddColumn<string>(
                name: "CustomerUsername",
                table: "CustomerPayments",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerUsername",
                table: "CustomerDebts",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CustomerUsername",
                table: "CustomerPayments",
                column: "CustomerUsername");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebts_CustomerUsername",
                table: "CustomerDebts",
                column: "CustomerUsername");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDebts_Users_CustomerUsername",
                table: "CustomerDebts",
                column: "CustomerUsername",
                principalTable: "Users",
                principalColumn: "Username",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPayments_Users_CustomerUsername",
                table: "CustomerPayments",
                column: "CustomerUsername",
                principalTable: "Users",
                principalColumn: "Username",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
