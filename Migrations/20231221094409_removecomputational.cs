using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class removecomputational : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Insp",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tadis",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tbill",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tdis",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tocv",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Todam",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tonw",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Torv",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tprdis",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tvam",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Tvop",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Adis",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Cop",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Tsstam",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Vam",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Vop",
                table: "InvoiceItems");

            migrationBuilder.AddColumn<string>(
                name: "EconomicCode",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Mobile",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PostCode",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TaxId",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EconomicCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Mobile",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PostCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "Users");

            migrationBuilder.AddColumn<decimal>(
                name: "Insp",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tadis",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tbill",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tdis",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tocv",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Todam",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tonw",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Torv",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tprdis",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tvam",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tvop",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Adis",
                table: "InvoiceItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Cop",
                table: "InvoiceItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tsstam",
                table: "InvoiceItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Vam",
                table: "InvoiceItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Vop",
                table: "InvoiceItems",
                type: "decimal(18,2)",
                nullable: true);
        }
    }
}
