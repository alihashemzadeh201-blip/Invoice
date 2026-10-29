using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
        public int ExpiredSkippedCount { get; set; }
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

                // تشخیص ستون‌ها بر اساس هدرها (سازگار با فایل‌های stuffid و قالب‌های سفارشی)
                int codeCol = -1;
                int titleCol = -1;
                int vatRateCol = -1;
                int vatTaxCol = -1;
                int expireCol = -1;
                int typeCol = -1;
                int specialCol = -1;
                int customRateCol = -1;

                if (doc.ColumnHeaders != null && doc.ColumnHeaders.Any())
                {
                    for (int i = 0; i < doc.ColumnHeaders.Count; i++)
                    {
                        var h = (doc.ColumnHeaders[i] ?? "").Trim().ToLowerInvariant();
                        if (h == "descriptionofid" || (h.Contains("description") && !h.Contains("pricing")) || h.Contains("شرح") || h.Contains("عنوان") || h == "title" || h == "sstt" || (h.Contains("نام") && !h.Contains("نامعتبر")))
                        {
                            titleCol = i;
                        }
                        else if ((h == "id" || h == "code" || h == "sstid" || h.Contains("شناسه") || h.Contains("کد")) && !h.Contains("description") && !h.Contains("نوع") && !h.Contains("شرح"))
                        {
                            codeCol = i;
                        }
                        else if (h.Contains("expiration") || h.Contains("expire") || h.Contains("انقضا") || h.Contains("اعتبار"))
                        {
                            expireCol = i;
                        }
                        else if ((h.Contains("ارزش افزوده") || h.Contains("مالیات") || h == "vat" || h == "vatrate" || h == "vra") && !h.Contains("خاص") && !h.Contains("مشمول") && !h.Contains("taxable"))
                        {
                            vatRateCol = i;
                        }
                        else if (h.Contains("مشمول") || h == "vattax" || h == "taxable")
                        {
                            vatTaxCol = i;
                        }
                        else if (h.Contains("نوع") || h == "type")
                        {
                            typeCol = i;
                        }
                        else if (h == "خاص" || h == "special")
                        {
                            specialCol = i;
                        }
                        else if (h.Contains("نرخ خاص") || h.Contains("مالیات خاص") || h == "vatcustomrate")
                        {
                            customRateCol = i;
                        }
                    }
                }

                // مقادیر پیش‌فرض در صورت عدم وجود نام هدر مشخص
                if (codeCol < 0) codeCol = 0;
                if (titleCol < 0) titleCol = 1;
                if (vatRateCol < 0) vatRateCol = 2;
                if (vatTaxCol < 0) vatTaxCol = 3;
                if (typeCol < 0) typeCol = (doc.ColumnHeaders != null && doc.ColumnHeaders.Count >= 9) ? 8 : 3;

                // خواندن و اعتبارسنجی ردیف‌ها
                var parsedItems = new Dictionary<string, Stuff>(StringComparer.OrdinalIgnoreCase);
                var today = DateTime.Today;

                foreach (var row in doc.Rows)
                {
                    if (row.Cells == null || row.Cells.Count == 0) continue;

                    // بررسی تاریخ انقضا: مواردی که تاریخ انقضای آنها گذشته باشد بارگذاری نمی‌شوند
                    if (expireCol >= 0 && row.Cells.Count > expireCol)
                    {
                        var rawExpire = row.Cells[expireCol];
                        if (IsExpired(rawExpire, today))
                        {
                            ExpiredSkippedCount++;
                            continue;
                        }
                    }

                    string? rawCode = row.Cells.Count > codeCol ? row.Cells[codeCol] : null;
                    string? rawTitle = row.Cells.Count > titleCol ? row.Cells[titleCol] : null;

                    var cleanCode = NormalizeStuffCode(rawCode);
                    if (string.IsNullOrWhiteSpace(cleanCode) || string.IsNullOrWhiteSpace(rawTitle))
                        continue;

                    var cleanTitle = rawTitle.Trim();

                    // نرخ مالیات بر ارزش افزوده
                    int vatRate = 0;
                    if (vatRateCol >= 0 && row.Cells.Count > vatRateCol)
                    {
                        vatRate = ParseVatRate(row.Cells[vatRateCol]);
                    }

                    // نوع شناسه
                    StuffType stuffType = StuffType.شناسه_عمومی_تولید_داخل_کالا;
                    if (typeCol >= 0 && row.Cells.Count > typeCol)
                    {
                        stuffType = ParseStuffType(row.Cells[typeCol]);
                    }

                    // مشمولیت ارزش افزوده
                    bool vatTax = true;
                    if (vatTaxCol >= 0 && row.Cells.Count > vatTaxCol)
                    {
                        vatTax = ParseTaxable(row.Cells[vatTaxCol]);
                    }

                    // کالای خاص
                    bool isSpecial = false;
                    if (specialCol >= 0 && row.Cells.Count > specialCol)
                    {
                        var rawSpecial = (row.Cells[specialCol] ?? "").Trim().ToLowerInvariant();
                        if (rawSpecial == "بله" || rawSpecial == "دارد" || rawSpecial == "1" || rawSpecial == "true")
                        {
                            isSpecial = true;
                        }
                    }

                    // نرخ مالیات خاص
                    int vatCustomRate = 0;
                    if (customRateCol >= 0 && row.Cells.Count > customRateCol)
                    {
                        vatCustomRate = ParseVatRate(row.Cells[customRateCol]);
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
                    if (ExpiredSkippedCount > 0)
                    {
                        ErrorMessage = $"تمامی ردیف‌های فایل ({ExpiredSkippedCount:N0} مورد) دارای تاریخ انقضای گذشته بودند و بارگذاری نشدند.";
                    }
                    else
                    {
                        ErrorMessage = "هیچ ردیف معتبری در فایل اکسل شناسایی نشد. لطفاً از وجود ستون‌های شناسه و شرح اطمینان حاصل فرمایید.";
                    }
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
                    $"بارگذاری موفقیت‌آمیز {InsertedCount} شناسه جدید، بروزرسانی {UpdatedCount} شناسه و نادیده گرفتن {ExpiredSkippedCount} مورد منقضی شده از فایل {ExcelFile.FileName}",
                    entityId: InsertedCount.ToString());

                var resultDetails = new List<string>();
                resultDetails.Add($"{InsertedCount:N0} شناسه جدید افزوده شد");
                if (UpdatedCount > 0) resultDetails.Add($"{UpdatedCount:N0} مورد بروزرسانی گردید");
                if (ExpiredSkippedCount > 0) resultDetails.Add($"{ExpiredSkippedCount:N0} مورد منقضی‌شده نادیده گرفته شد");
                if (SkippedCount > 0) resultDetails.Add($"{SkippedCount:N0} مورد تکراری نادیده گرفته شد");

                SuccessMessage = "عملیات بارگذاری با موفقیت انجام شد: " + string.Join("، ", resultDetails) + ".";

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

        public static string? NormalizeStuffCode(string? rawCode)
        {
            if (string.IsNullOrWhiteSpace(rawCode)) return null;

            var clean = rawCode.Trim();
            clean = Utils.ConvertPersianNumberToEnglish(clean);
            clean = clean.Replace(" ", "").Replace("-", "").Replace("_", "");

            if (string.IsNullOrWhiteSpace(clean)) return null;

            // پشتیبانی از نماد علمی مانند 2.90395E+12 یا 2.8E+12
            if (clean.Contains("E+", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("E-", StringComparison.OrdinalIgnoreCase) ||
                (clean.Contains("E", StringComparison.OrdinalIgnoreCase) && char.IsDigit(clean[^1])))
            {
                if (decimal.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal sciDec))
                {
                    clean = sciDec.ToString("F0", CultureInfo.InvariantCulture);
                }
            }
            else if (clean.Contains('.') || clean.Contains(','))
            {
                if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decVal))
                {
                    clean = decVal.ToString("F0", CultureInfo.InvariantCulture);
                }
            }

            if (clean.Length < 3) return null;
            return clean;
        }

        public static bool IsExpired(string? rawExpireDate, DateTime? referenceDate = null)
        {
            if (string.IsNullOrWhiteSpace(rawExpireDate)) return false;

            var clean = rawExpireDate.Trim();
            clean = Utils.ConvertPersianNumberToEnglish(clean);
            if (clean == "-" || clean == "ندارد" || clean.Equals("null", StringComparison.OrdinalIgnoreCase) || clean.Equals("none", StringComparison.OrdinalIgnoreCase))
                return false;

            var today = (referenceDate ?? DateTime.Today).Date;

            clean = clean.Replace("/", "-");
            var parts = clean.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) &&
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int d))
            {
                // تاریخ شمسی (۱۳۰۰ تا ۱۵۰۰)
                if (y >= 1300 && y <= 1500)
                {
                    try
                    {
                        var pc = new PersianCalendar();
                        int clampedMonth = Math.Clamp(m, 1, 12);
                        int maxDays = pc.GetDaysInMonth(y, clampedMonth);
                        int clampedDay = Math.Clamp(d, 1, maxDays);
                        var expireDate = pc.ToDateTime(y, clampedMonth, clampedDay, 0, 0, 0, 0);
                        return expireDate.Date < today;
                    }
                    catch
                    {
                        var pc = new PersianCalendar();
                        string todayShamsi = $"{pc.GetYear(today):D4}-{pc.GetMonth(today):D2}-{pc.GetDayOfMonth(today):D2}";
                        string itemShamsi = $"{y:D4}-{m:D2}-{d:D2}";
                        return string.CompareOrdinal(itemShamsi, todayShamsi) < 0;
                    }
                }

                // تاریخ میلادی (۱۹۰۰ تا ۲۱۰۰)
                if (y >= 1900 && y <= 2100)
                {
                    try
                    {
                        int clampedMonth = Math.Clamp(m, 1, 12);
                        int maxDays = DateTime.DaysInMonth(y, clampedMonth);
                        int clampedDay = Math.Clamp(d, 1, maxDays);
                        var expireDate = new DateTime(y, clampedMonth, clampedDay);
                        return expireDate.Date < today;
                    }
                    catch
                    {
                        // نادیده گرفتن خطا
                    }
                }
            }

            // تاریخ به صورت OADate عددی اکسل
            if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double oaDate) && oaDate > 30000 && oaDate < 100000)
            {
                try
                {
                    return DateTime.FromOADate(oaDate).Date < today;
                }
                catch
                {
                    // نادیده گرفتن خطا
                }
            }

            // تاریخ به صورت فرمت استاندارد میلادی
            if (DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt.Date < today;
            }

            return false;
        }

        public static StuffType ParseStuffType(string? rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType)) return StuffType.شناسه_عمومی_تولید_داخل_کالا;
            var clean = rawType.Trim().Replace(" ", "_");
            if (Enum.TryParse<StuffType>(clean, true, out var parsed)) return parsed;

            bool isService = rawType.Contains("خدمت");
            bool isExclusive = rawType.Contains("اختصاصی");
            bool isImported = rawType.Contains("وارداتی");

            if (isService)
            {
                if (isExclusive)
                    return isImported ? StuffType.شناسه_اختصاصی_وارداتی_خدمت : StuffType.شناسه_اختصاصی_تولید_داخل_خدمت;
                else
                    return isImported ? StuffType.شناسه_عمومی_وارداتی_خدمت : StuffType.شناسه_عمومی_تولید_داخل_خدمت;
            }
            else
            {
                if (isExclusive)
                    return isImported ? StuffType.شناسه_اختصاصی_وارداتی_کالا : StuffType.شناسه_اختصاصی_تولید_داخل_کالا;
                else
                    return isImported ? StuffType.شناسه_عمومی_وارداتی_کالا : StuffType.شناسه_عمومی_تولید_داخل_کالا;
            }
        }

        public static bool ParseTaxable(string? rawTaxable)
        {
            if (string.IsNullOrWhiteSpace(rawTaxable)) return true;
            var clean = rawTaxable.Trim().ToLowerInvariant();
            if (clean == "معاف" || clean == "غیر مشمول" || clean == "خیر" || clean == "0" || clean == "false" || clean == "exempt")
                return false;
            return true;
        }

        public static int ParseVatRate(string? rawVat)
        {
            if (string.IsNullOrWhiteSpace(rawVat)) return 0;
            var clean = Utils.ConvertPersianNumberToEnglish(rawVat).Replace("%", "").Replace("درصد", "").Trim();
            if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d)) return (int)Math.Round(d);
            return 0;
        }
    }
}
