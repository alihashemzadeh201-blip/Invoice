using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class vallet : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Vallet",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Price",
                table: "CreditRequests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "Months",
                table: "CreditRequests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "PeriodId",
                table: "CreditRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ByCash = table.Column<bool>(type: "bit", nullable: false),
                    Price = table.Column<int>(type: "int", nullable: false),
                    CustomerUsername = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BankTransactionReceipt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceCardNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckedByUsername = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Confirm = table.Column<bool>(type: "bit", nullable: true),
                    ConfirmDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Users_CustomerUsername",
                        column: x => x.CustomerUsername,
                        principalTable: "Users",
                        principalColumn: "Username",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditRequests_PeriodId",
                table: "CreditRequests",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CustomerUsername",
                table: "Transactions",
                column: "CustomerUsername");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditRequests_CreditPeriods_PeriodId",
                table: "CreditRequests",
                column: "PeriodId",
                principalTable: "CreditPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditRequests_CreditPeriods_PeriodId",
                table: "CreditRequests");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_CreditRequests_PeriodId",
                table: "CreditRequests");

            migrationBuilder.DropColumn(
                name: "Vallet",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PeriodId",
                table: "CreditRequests");

            migrationBuilder.AlterColumn<int>(
                name: "Price",
                table: "CreditRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Months",
                table: "CreditRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
