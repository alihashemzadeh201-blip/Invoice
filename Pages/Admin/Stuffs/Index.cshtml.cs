using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.Admin.Stuffs
{
    [Authorize(Roles = "admin")]
    public class IndexModel : PageModel
    {
        private readonly DataContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            DataContext context,
            IUserActivityLogger activityLogger,
            ILogger<IndexModel> logger)
        {
            _context = context;
            _activityLogger = activityLogger;
            _logger = logger;
        }

        public List<Stuff> Stuffs { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 50;
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));

        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (PageNumber < 1) PageNumber = 1;

            var query = _context.Stuffs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var term = Search.Trim();
                query = query.Where(s => s.Code.Contains(term) || s.Title.Contains(term));
            }

            TotalCount = await query.CountAsync();

            Stuffs = await query
                .OrderBy(s => s.Code)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return RedirectToPage(new { Search, PageNumber });
            }

            var item = await _context.Stuffs.FindAsync(code);
            if (item != null)
            {
                _context.Stuffs.Remove(item);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(
                    "شناسه کالا",
                    "حذف شناسه کالا",
                    $"حذف شناسه کالا {code} ({item.Title}) توسط مدیر",
                    entityId: code);

                SuccessMessage = $"شناسه کالا {code} با موفقیت حذف گردید.";
            }

            return RedirectToPage(new { Search, PageNumber });
        }

        public IActionResult OnGetDownloadTemplate()
        {
            try
            {
                var doc = new ExcelDoc
                {
                    Title = "قالب استاندارد بارگذاری شناسه‌های کالا و خدمات سامانه مودیان"
                };

                doc.ColumnHeaders.AddRange(new[]
                {
                    "شناسه کالا/خدمت",
                    "شرح کالا/خدمت",
                    "نرخ مالیات بر ارزش افزوده",
                    "نوع شناسه",
                    "مشمول ارزش افزوده",
                    "خاص",
                    "نرخ مالیات خاص"
                });

                // ردیف‌های نمونه
                var sampleRow1 = new ExcelRow();
                sampleRow1.Cells.AddRange(new[]
                {
                    "2720000123456",
                    "کالای نمونه تستی تولید داخل",
                    "10",
                    "شناسه_عمومی_تولید_داخل_کالا",
                    "بله",
                    "خیر",
                    "0"
                });
                doc.Rows.Add(sampleRow1);

                var sampleRow2 = new ExcelRow();
                sampleRow2.Cells.AddRange(new[]
                {
                    "2720000654321",
                    "خدمت نمونه مشاوره و پشتیبانی",
                    "10",
                    "شناسه_عمومی_تولید_داخل_خدمت",
                    "بله",
                    "خیر",
                    "0"
                });
                doc.Rows.Add(sampleRow2);

                Directory.CreateDirectory("Uploads");
                var tempPath = Path.Combine("Uploads", $"Template_Stuffs_{Guid.NewGuid():N}.xlsx");
                ExcelHelp.Save(doc, tempPath);

                var bytes = System.IO.File.ReadAllBytes(tempPath);
                try { System.IO.File.Delete(tempPath); } catch { }

                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Stuffs_Import_Template.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در تولید فایل اکسل نمونه کالاها.");
                ErrorMessage = "خطا در تولید فایل اکسل نمونه.";
                return RedirectToPage();
            }
        }
    }
}
