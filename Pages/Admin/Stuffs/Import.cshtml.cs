using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.Admin.Stuffs
{
    [Authorize(Roles = "admin")]
    public class ImportModel : PageModel
    {
        private readonly DataContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILogger<ImportModel> _logger;

        public ImportModel(
            DataContext context,
            IUserActivityLogger activityLogger,
            ILogger<ImportModel> logger)
        {
            _context = context;
            _activityLogger = activityLogger;
            _logger = logger;
        }

        [BindProperty]
        [Required(ErrorMessage = "لطفاً فایل اکسل (.xlsx) را انتخاب نمایید.")]
        [Display(Name = "فایل اکسل کالاها و خدمات")]
        public IFormFile? ExcelFile { get; set; }

        [BindProperty]
        [Display(Name = "در صورت وجود شناسه تکراری، مشخصات آن بروزرسانی شود")]
        public bool UpdateExisting { get; set; } = true;

        public int InsertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int TotalProcessedCount { get; set; }
        public List<string> ImportErrors { get; set; } = new();

        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ExcelFile == null || ExcelFile.Length == 0)
            {
                ModelState.AddModelError("ExcelFile", "لطفاً یک فایل اکسل معتبر انتخاب نمایید.");
                return Page();
            }

            var extension = Path.GetExtension(ExcelFile.FileName).ToLowerInvariant();
            if (extension != ".xlsx")
            {
                ModelState.AddModelError("ExcelFile", "فرمت فایل باید اکسل (.xlsx) باشد.");
                return Page();
            }

            Directory.CreateDirectory("Uploads");
            var tempPath = Path.Combine("Uploads", $"Stuffs_Upload_{Guid.NewGuid():N}.xlsx");

            try
            {
                await using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await ExcelFile.CopyToAsync(stream);
                }

                var doc = ExcelHelp.Open(tempPath);
                if (doc == null || doc.Rows == null || !doc.Rows.Any())
                {
                    ErrorMessage = "فایل اکسل ارسالی خالی است یا ساختار استانداردی ندارد.";
                    return Page();
                }

                // تشخیص ستون‌ها بر اساس هدرها
                int codeCol = 0;
                int titleCol = 1;
                int vatRateCol = 2;
                int typeCol = 3;
                int vatTaxCol = 4;
                int specialCol = 5;
                int customRateCol = 6;

                if (doc.ColumnHeaders != null && doc.ColumnHeaders.Any())
                {
                    for (int i = 0; i < doc.ColumnHeaders.Count; i++)
                    {
                        var h = (doc.ColumnHeaders[i] ?? "").Trim().ToLowerInvariant();
                        if (h.Contains("شناسه") || h.Contains("کد") || h == "code" || h == "sstid")
                            codeCol = i;
                        else if (h.Contains("شرح") || h.Contains("عنوان") || h == "title" || h == "sstt" || h.Contains("نام"))
                            titleCol = i;
                        else if ((h.Contains("ارزش افزوده") || h.Contains("مالیات") || h == "vat" || h == "vatrate" || h == "vra") && !h.Contains("خاص") && !h.Contains("مشمول"))
                            vatRateCol = i;
                        else if (h.Contains("نوع") || h == "type")
                            typeCol = i;
                        else if (h.Contains("مشمول") || h == "vattax")
                            vatTaxCol = i;
                        else if (h == "خاص" || h == "special")
                            specialCol = i;
                        else if (h.Contains("نرخ خاص") || h.Contains("مالیات خاص") || h == "vatcustomrate")
                            customRateCol = i;
                    }
                }

                // خواندن و اعتبارسنجی ردیف‌ها
                var parsedItems = new Dictionary<string, Stuff>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in doc.Rows)
                {
                    if (row.Cells == null || row.Cells.Count == 0) continue;

                    string? rawCode = row.Cells.Count > codeCol ? row.Cells[codeCol]?.Trim() : null;
                    string? rawTitle = row.Cells.Count > titleCol ? row.Cells[titleCol]?.Trim() : null;

                    if (string.IsNullOrWhiteSpace(rawCode) || string.IsNullOrWhiteSpace(rawTitle))
                        continue;

                    // پاکسازی شناسه کالا (حذف فاصله‌ها و کاراکترهای اضافی)
                    var cleanCode = rawCode.Replace(" ", "").Replace("-", "").Trim();
                    if (cleanCode.Length < 3) continue;

                    var cleanTitle = rawTitle.Trim();

                    // نرخ مالیات بر ارزش افزوده
                    int vatRate = 0;
                    if (row.Cells.Count > vatRateCol)
                    {
                        var rawVat = (row.Cells[vatRateCol] ?? "").Replace("%", "").Replace("درصد", "").Trim();
                        if (int.TryParse(rawVat, out var parsedVat))
                        {
                            vatRate = parsedVat;
                        }
                    }

                    // نوع شناسه
                    StuffType stuffType = StuffType.شناسه_عمومی_تولید_داخل_کالا;
                    if (row.Cells.Count > typeCol)
                    {
                        var rawType = (row.Cells[typeCol] ?? "").Trim().Replace(" ", "_");
                        if (Enum.TryParse<StuffType>(rawType, true, out var parsedType))
                        {
                            stuffType = parsedType;
                        }
                    }

                    // مشمولیت ارزش افزوده
                    bool vatTax = true;
                    if (row.Cells.Count > vatTaxCol)
                    {
                        var rawVatTax = (row.Cells[vatTaxCol] ?? "").Trim().ToLowerInvariant();
                        if (rawVatTax == "خیر" || rawVatTax == "معاف" || rawVatTax == "0" || rawVatTax == "false")
                        {
                            vatTax = false;
                        }
                    }

                    // کالای خاص
                    bool isSpecial = false;
                    if (row.Cells.Count > specialCol)
                    {
                        var rawSpecial = (row.Cells[specialCol] ?? "").Trim().ToLowerInvariant();
                        if (rawSpecial == "بله" || rawSpecial == "دارد" || rawSpecial == "1" || rawSpecial == "true")
                        {
                            isSpecial = true;
                        }
                    }

                    // نرخ مالیات خاص
                    int vatCustomRate = 0;
                    if (row.Cells.Count > customRateCol)
                    {
                        var rawCustom = (row.Cells[customRateCol] ?? "").Replace("%", "").Trim();
                        if (int.TryParse(rawCustom, out var parsedCustom))
                        {
                            vatCustomRate = parsedCustom;
                        }
                    }

                    parsedItems[cleanCode] = new Stuff
                    {
                        Code = cleanCode,
                        Title = cleanTitle,
                        VatRate = vatRate,
                        Type = stuffType,
                        VatTax = vatTax,
                        Special = isSpecial,
                        VatCustomRate = vatCustomRate
                    };
                }

                TotalProcessedCount = parsedItems.Count;
                if (TotalProcessedCount == 0)
                {
                    ErrorMessage = "هیچ ردیف معتبری در فایل اکسل شناسایی نشد. لطفاً از وجود ستون‌های شناسه و شرح اطمینان حاصل فرمایید.";
                    return Page();
                }

                // درج و بروزرسانی دسته‌ای (Chunking) در دیتابیس
                var chunks = parsedItems.Values.Chunk(250);
                foreach (var chunk in chunks)
                {
                    var chunkCodes = chunk.Select(c => c.Code).ToList();
                    var existingBatch = await _context.Stuffs
                        .Where(s => chunkCodes.Contains(s.Code))
                        .ToDictionaryAsync(s => s.Code, StringComparer.OrdinalIgnoreCase);

                    foreach (var item in chunk)
                    {
                        if (existingBatch.TryGetValue(item.Code, out var existing))
                        {
                            if (UpdateExisting)
                            {
                                existing.Title = item.Title;
                                existing.VatRate = item.VatRate;
                                existing.Type = item.Type;
                                existing.VatTax = item.VatTax;
                                existing.Special = item.Special;
                                existing.VatCustomRate = item.VatCustomRate;
                                UpdatedCount++;
                            }
                            else
                            {
                                SkippedCount++;
                            }
                        }
                        else
                        {
                            _context.Stuffs.Add(item);
                            InsertedCount++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    _context.ChangeTracker.Clear();
                }

                await _activityLogger.LogAsync(
                    "شناسه کالا",
                    "بارگذاری اکسل شناسه کالاها",
                    $"بارگذاری موفقیت‌آمیز {InsertedCount} شناسه جدید و بروزرسانی {UpdatedCount} شناسه از فایل {ExcelFile.FileName}",
                    entityId: InsertedCount.ToString());

                SuccessMessage = $"عملیات بارگذاری با موفقیت انجام شد: {InsertedCount:N0} شناسه جدید افزوده شد" +
                                 (UpdatedCount > 0 ? $"، {UpdatedCount:N0} مورد بروزرسانی گردید" : "") +
                                 (SkippedCount > 0 ? $"، {SkippedCount:N0} مورد تکراری نادیده گرفته شد" : "") + ".";

                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش و بارگذاری فایل اکسل کالاها.");
                ErrorMessage = "خطا در پردازش فایل اکسل: " + ex.Message;
                return Page();
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tempPath))
                    {
                        System.IO.File.Delete(tempPath);
                    }
                }
                catch
                {
                    // نادیده گرفتن خطای حذف فایل موقت
                }
            }
        }
    }
}
