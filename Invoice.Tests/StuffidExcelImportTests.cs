using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoice.Pages.Admin.Stuffs;
using Invoices.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class StuffidExcelImportTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DataContext _context;
        private readonly Mock<IUserActivityLogger> _activityLoggerMock;
        private readonly Mock<ILogger<ImportModel>> _loggerMock;

        public StuffidExcelImportTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<DataContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new DataContext(options);
            _context.Database.EnsureCreated();

            _activityLoggerMock = new Mock<IUserActivityLogger>();
            _loggerMock = new Mock<ILogger<ImportModel>>();
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        [Theory]
        [InlineData("2.90395E+12", "2903950000000")]
        [InlineData("2.80001E+12", "2800010000000")]
        [InlineData("2.8E+12", "2800000000000")]
        [InlineData("2.00993E+12", "2009930000000")]
        [InlineData("2720000123456", "2720000123456")]
        [InlineData("2720000123456.0", "2720000123456")]
        [InlineData("۲۷۲۰۰۰۰۱۲۳۴۵۶", "2720000123456")]
        public void NormalizeStuffCode_ValidInputs_ReturnsNormalizedCode(string input, string expected)
        {
            var result = ImportModel.NormalizeStuffCode(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void NormalizeStuffCode_NullOrShortInputs_ReturnsNull()
        {
            Assert.Null(ImportModel.NormalizeStuffCode(null));
            Assert.Null(ImportModel.NormalizeStuffCode(""));
            Assert.Null(ImportModel.NormalizeStuffCode("  "));
            Assert.Null(ImportModel.NormalizeStuffCode("12"));
        }

        [Fact]
        public void IsExpired_PastShamsiDate_ReturnsTrue()
        {
            // تاریخ 1402-12-29 مربوط به گذشته است
            var today = new DateTime(2026, 10, 29); // معادل آبان 1405
            Assert.True(ImportModel.IsExpired("1402-12-29", today));
            Assert.True(ImportModel.IsExpired("1402/12/29", today));
            Assert.True(ImportModel.IsExpired("۱۴۰۲/۱۲/۲۹", today));
        }

        [Fact]
        public void IsExpired_EmptyOrNullOrHyphen_ReturnsFalse()
        {
            var today = new DateTime(2026, 10, 29);
            Assert.False(ImportModel.IsExpired(null, today));
            Assert.False(ImportModel.IsExpired("", today));
            Assert.False(ImportModel.IsExpired("   ", today));
            Assert.False(ImportModel.IsExpired("-", today));
            Assert.False(ImportModel.IsExpired("ندارد", today));
        }

        [Fact]
        public void IsExpired_FutureShamsiDate_ReturnsFalse()
        {
            var today = new DateTime(2026, 10, 29); // 1405-08-08
            Assert.False(ImportModel.IsExpired("1408-01-01", today));
            Assert.False(ImportModel.IsExpired("1405-12-29", today));
        }

        [Fact]
        public void ParseStuffType_VariousPersianDescriptions_MapsCorrectly()
        {
            Assert.Equal(StuffType.شناسه_اختصاصی_تولید_داخل_کالا, ImportModel.ParseStuffType("شناسه اختصاصی تولید داخل"));
            Assert.Equal(StuffType.شناسه_اختصاصی_وارداتی_کالا, ImportModel.ParseStuffType("شناسه اختصاصی وارداتی"));
            Assert.Equal(StuffType.شناسه_عمومی_تولید_داخل_کالا, ImportModel.ParseStuffType("شناسه عمومی تولید داخل"));
            Assert.Equal(StuffType.شناسه_عمومی_وارداتی_کالا, ImportModel.ParseStuffType("شناسه عمومی وارداتی"));
            Assert.Equal(StuffType.شناسه_اختصاصی_تولید_داخل_خدمت, ImportModel.ParseStuffType("شناسه اختصاصی تولید داخل خدمت"));
            Assert.Equal(StuffType.شناسه_عمومی_وارداتی_خدمت, ImportModel.ParseStuffType("شناسه عمومی وارداتی خدمت"));
        }

        [Fact]
        public void ParseTaxable_And_ParseVatRate_WorkAsExpected()
        {
            Assert.True(ImportModel.ParseTaxable("مشمول"));
            Assert.True(ImportModel.ParseTaxable(""));
            Assert.False(ImportModel.ParseTaxable("معاف"));
            Assert.False(ImportModel.ParseTaxable("غیر مشمول"));

            Assert.Equal(9, ImportModel.ParseVatRate("9"));
            Assert.Equal(10, ImportModel.ParseVatRate("10%"));
            Assert.Equal(10, ImportModel.ParseVatRate("۱۰ درصد"));
            Assert.Equal(0, ImportModel.ParseVatRate("0"));
        }

        [Fact]
        public void ExcelHelp_GetColumnIndex_CalculatesCorrectly()
        {
            Assert.Equal(0, ExcelHelp.GetColumnIndex("A1"));
            Assert.Equal(1, ExcelHelp.GetColumnIndex("B2"));
            Assert.Equal(5, ExcelHelp.GetColumnIndex("F10"));
            Assert.Equal(25, ExcelHelp.GetColumnIndex("Z1"));
            Assert.Equal(26, ExcelHelp.GetColumnIndex("AA1"));
        }

        [Fact]
        public async Task ImportModel_ExactStuffidExcel_ImportsActiveAndSkipsExpired()
        {
            // Arrange
            // ساخت یک سند اکسل با دقیقاً همان ساختار خروجی stuffid ارسالی کاربر
            var tempExcelPath = Path.Combine(Path.GetTempPath(), $"stuffid_test_{Guid.NewGuid():N}.xlsx");
            try
            {
                var doc = new ExcelDoc();
                doc.ColumnHeaders.AddRange(new[]
                {
                    "ID",
                    "DescriptionOfID",
                    "Vat",
                    "Taxable",
                    "RunDate",
                    "ExpirationDate",
                    "CreateDate",
                    "LastEditDate",
                    "Type",
                    "PricingDescription"
                });

                // ردیف ۱: منقضی شده (1402-12-29) -> باید فیلتر شود
                var r1 = new ExcelRow();
                r1.Cells.AddRange(new[] { "2.90395E+12", "لوله ساده فولادی، درز مستقیم", "9", "مشمول", "1402-03-17", "1402-12-29", "1404-06-05", "1402-03-17", "شناسه اختصاصی تولید داخل", "" });
                doc.Rows.Add(r1);

                // ردیف ۲: معتبر بدون تاریخ انقضا -> باید بارگذاری شود
                var r2 = new ExcelRow();
                r2.Cells.AddRange(new[] { "2.80001E+12", "بلبرینگ شعاعی، برند SKF", "10", "مشمول", "1403-03-01", "", "1404-06-05", "1403-03-01", "شناسه اختصاصی وارداتی", "" });
                doc.Rows.Add(r2);

                // ردیف ۳: منقضی شده (1402-12-29) -> باید فیلتر شود
                var r3 = new ExcelRow();
                r3.Cells.AddRange(new[] { "2.8E+12", "مجموعه، KD، نگهدارنده Z", "9", "مشمول", "1402-04-28", "1402-12-29", "1404-06-05", "1402-04-28", "شناسه اختصاصی وارداتی", "" });
                doc.Rows.Add(r3);

                // ردیف ۴: منقضی شده (1402-12-29) -> باید فیلتر شود
                var r4 = new ExcelRow();
                r4.Cells.AddRange(new[] { "2.00993E+12", "بلوز زنانه، استین بلند", "9", "مشمول", "1402-02-11", "1402-12-29", "1404-06-05", "1402-02-11", "شناسه اختصاصی وارداتی", "" });
                doc.Rows.Add(r4);

                // ردیف ۵: معتبر بدون تاریخ انقضا -> باید بارگذاری شود
                var r5 = new ExcelRow();
                r5.Cells.AddRange(new[] { "2.90179E+12", "مخزن تحت فشار فولاد ضد زنگ", "10", "مشمول", "1404-02-18", "", "1404-06-05", "1404-02-18", "شناسه اختصاصی تولید داخل", "" });
                doc.Rows.Add(r5);

                // ردیف ۶: معتبر بدون تاریخ انقضا -> باید بارگذاری شود
                var r6 = new ExcelRow();
                r6.Cells.AddRange(new[] { "2.00573E+12", "شلوار مردانه، راحتی", "10", "مشمول", "1403-01-01", "", "1404-06-05", "1403-01-01", "شناسه اختصاصی وارداتی", "" });
                doc.Rows.Add(r6);

                // ردیف ۷: منقضی شده (1402-12-29) -> باید فیلتر شود
                var r7 = new ExcelRow();
                r7.Cells.AddRange(new[] { "2.8E+12", "بدنه، منفرد، تیوب لامپ ال ای دی", "9", "مشمول", "1402-05-11", "1402-12-29", "1404-06-05", "1402-05-11", "شناسه اختصاصی وارداتی", "" });
                doc.Rows.Add(r7);

                // ردیف ۸: معتبر بدون تاریخ انقضا -> باید بارگذاری شود
                var r8 = new ExcelRow();
                r8.Cells.AddRange(new[] { "2.90744E+12", "مخزن تحت فشار، فولاد گالوانیزه", "10", "مشمول", "1403-05-16", "", "1404-06-05", "1403-05-16", "شناسه اختصاصی تولید داخل", "" });
                doc.Rows.Add(r8);

                ExcelHelp.Save(doc, tempExcelPath);

                // ایجاد IFormFile تستی
                using var fs = File.OpenRead(tempExcelPath);
                var formFile = new FormFile(fs, 0, fs.Length, "ExcelFile", "stuffid_export.xlsx")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                };

                var pageModel = new ImportModel(_context, _activityLoggerMock.Object, _loggerMock.Object)
                {
                    ExcelFile = formFile,
                    UpdateExisting = true
                };

                // Act
                var result = await pageModel.OnPostAsync();

                // Assert
                Assert.True(string.IsNullOrEmpty(pageModel.ErrorMessage), $"ErrorMessage: {pageModel.ErrorMessage}");
                Assert.IsType<RedirectToPageResult>(result);
                // دقیقا ۴ مورد فعال باید اضافه شده باشند
                Assert.Equal(4, pageModel.InsertedCount);
                // دقیقا ۴ مورد منقضی شده با تاریخ 1402-12-29 باید فیلتر شده باشند
                Assert.Equal(4, pageModel.ExpiredSkippedCount);
                Assert.IsType<RedirectToPageResult>(result);
                // دقیقا ۴ مورد فعال باید اضافه شده باشند
                Assert.Equal(4, pageModel.InsertedCount);
                // دقیقا ۴ مورد منقضی شده با تاریخ 1402-12-29 باید فیلتر شده باشند
                Assert.Equal(4, pageModel.ExpiredSkippedCount);

                // بررسی وجود شناسه‌های معتبر در دیتابیس
                var importedStuffs = await _context.Stuffs.ToListAsync();
                Assert.Equal(4, importedStuffs.Count);

                // بررسی کدهای نرمال‌شده (تبدیل شده از نماد علمی)
                Assert.Contains(importedStuffs, s => s.Code == "2800010000000" && s.VatRate == 10 && s.Type == StuffType.شناسه_اختصاصی_وارداتی_کالا);
                Assert.Contains(importedStuffs, s => s.Code == "2901790000000" && s.VatRate == 10 && s.Type == StuffType.شناسه_اختصاصی_تولید_داخل_کالا);
                Assert.Contains(importedStuffs, s => s.Code == "2005730000000" && s.VatRate == 10 && s.Type == StuffType.شناسه_اختصاصی_وارداتی_کالا);
                Assert.Contains(importedStuffs, s => s.Code == "2907440000000" && s.VatRate == 10 && s.Type == StuffType.شناسه_اختصاصی_تولید_داخل_کالا);

                // بررسی عدم وجود اقلام منقضی در دیتابیس
                Assert.DoesNotContain(importedStuffs, s => s.Code == "2903950000000");
                Assert.DoesNotContain(importedStuffs, s => s.Code == "2009930000000");
            }
            finally
            {
                if (File.Exists(tempExcelPath))
                {
                    try { File.Delete(tempExcelPath); } catch { }
                }
            }
        }
    }
}
