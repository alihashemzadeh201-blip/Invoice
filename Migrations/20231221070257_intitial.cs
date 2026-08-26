using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invoice.Migrations
{
    public partial class intitial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Taxid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Indatim = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Indati2m = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Inty = table.Column<int>(type: "int", nullable: false),
                    Inno = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Irtaxid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Inp = table.Column<int>(type: "int", nullable: false),
                    Ins = table.Column<int>(type: "int", nullable: false),
                    Tins = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tob = table.Column<int>(type: "int", nullable: false),
                    Bid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tinb = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sbc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Bpc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Bbc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ft = table.Column<int>(type: "int", nullable: true),
                    Bpn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Scln = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Scc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Crn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Billid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tprdis = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tdis = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tadis = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tvam = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Todam = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tbill = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Setm = table.Column<int>(type: "int", nullable: false),
                    Cap = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Insp = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tvop = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tax17 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Cdcn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cdcd = table.Column<int>(type: "int", nullable: true),
                    Tonw = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Torv = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tocv = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeasureUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasureUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Username);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Iinn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Acn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Trmn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Trn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pcn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pdt = table.Column<long>(type: "bigint", nullable: true),
                    Pmt = table.Column<int>(type: "int", nullable: true),
                    Pv = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Sstid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sstt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MuId = table.Column<int>(type: "int", nullable: true),
                    Am = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Cfee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Cut = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Exr = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Prdis = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Dis = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Adis = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Vra = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Vam = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Odt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Odr = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Odam = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Olt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Olr = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Olam = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Consfee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Spro = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Bros = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Cop = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Vop = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Bsrn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tsstam = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Nw = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Ssrv = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Sscv = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    pspd = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceItems_MeasureUnits_MuId",
                        column: x => x.MuId,
                        principalTable: "MeasureUnits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_InvoiceId",
                table: "InvoiceItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_MuId",
                table: "InvoiceItems",
                column: "MuId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId",
                table: "Payments",
                column: "InvoiceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceItems");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "MeasureUnits");

            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}
