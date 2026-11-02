using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.Admin.Stuffs
{
    public class StuffImportResult
    {
        public int InsertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int ExpiredSkippedCount { get; set; }
        public int SkippedCount { get; set; }
        public int TotalProcessed { get; set; }
    }
    public class StuffImportProgressState
    {
        public string TaskId { get; set; } = string.Empty;
        public string Stage { get; set; } = "uploading"; // uploading, reading, saving, completed, error
        public string StageTitle { get; set; } = "در حال بارگذاری فایل...";
        public int ProcessedRows { get; set; }
        public int TotalEstimatedRows { get; set; }
        public int InsertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int ExpiredSkippedCount { get; set; }
        public int SkippedCount { get; set; }
        public int Percent { get; set; }
        public bool IsCompleted { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResultMessage { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public static class StuffImportProgressTracker
    {
        private static readonly ConcurrentDictionary<string, StuffImportProgressState> _tasks = new();

        public static StuffImportProgressState GetOrCreate(string taskId)
        {
            return _tasks.GetOrAdd(taskId, id => new StuffImportProgressState { TaskId = id });
        }

        public static void Update(string taskId, Action<StuffImportProgressState> updateAction)
        {
            if (_tasks.TryGetValue(taskId, out var state))
            {
                updateAction(state);
                state.LastUpdated = DateTime.UtcNow;
            }
        }

        public static StuffImportProgressState? Get(string taskId)
        {
            _tasks.TryGetValue(taskId, out var state);
            return state;
        }

        public static void Remove(string taskId)
        {
            _tasks.TryRemove(taskId, out _);
        }
    }

    [Authorize(Roles = "admin")]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public class ImportModel : PageModel
    {
        private readonly DataContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILogger<ImportModel> _logger;
        private readonly IServiceScopeFactory? _scopeFactory;

        public ImportModel(
            DataContext context,
            IUserActivityLogger activityLogger,
            ILogger<ImportModel> logger,
            IServiceScopeFactory? scopeFactory = null)
        {
            _context = context;
            _activityLogger = activityLogger;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        [BindProperty]
        [Required(ErrorMessage = "لطفاً فایل اکسل (.xlsx) را انتخاب نمایید.")]
        [Display(Name = "فایل اکسل کالاها و خدمات")]
        public IFormFile? ExcelFile { get; set; }

        [BindProperty]
        [Display(Name = "در صورت وجود شناسه تکراری، مشخصات آن بروزرسانی شود")]
        public bool UpdateExisting { get; set; } = true;

        [BindProperty(SupportsGet = true)]
        public string? TaskId { get; set; }

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

        public IActionResult OnGetProgress(string? taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                return new JsonResult(new { stage = "none", percent = 0 });

            var progress = StuffImportProgressTracker.Get(taskId);
            if (progress == null)
                return new JsonResult(new { stage = "none", percent = 0 });

            return new JsonResult(new
            {
                stage = progress.Stage,
                stageTitle = progress.StageTitle,
                processedRows = progress.ProcessedRows,
                totalEstimatedRows = progress.TotalEstimatedRows,
                insertedCount = progress.InsertedCount,
                updatedCount = progress.UpdatedCount,
                expiredSkippedCount = progress.ExpiredSkippedCount,
                skippedCount = progress.SkippedCount,
                percent = progress.Percent,
                isCompleted = progress.IsCompleted,
                errorMessage = progress.ErrorMessage,
                resultMessage = progress.ResultMessage
            });
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct = default)
        {
            var isAjax = Request?.Headers != null &&
                         (Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers.Accept.Any(a => a != null && a.Contains("application/json")));

            if (ExcelFile == null || ExcelFile.Length == 0)
            {
                var msg = "لطفاً یک فایل اکسل معتبر انتخاب نمایید.";
                if (isAjax) return new JsonResult(new { success = false, message = msg });
                ModelState.AddModelError("ExcelFile", msg);
                return Page();
            }

            var extension = Path.GetExtension(ExcelFile.FileName).ToLowerInvariant();
            if (extension != ".xlsx")
            {
                var msg = "فرمت فایل باید اکسل (.xlsx) باشد.";
                if (isAjax) return new JsonResult(new { success = false, message = msg });
                ModelState.AddModelError("ExcelFile", msg);
                return Page();
            }

            var effectiveTaskId = !string.IsNullOrWhiteSpace(TaskId) ? TaskId.Trim() : Guid.NewGuid().ToString("N");
            var progress = StuffImportProgressTracker.GetOrCreate(effectiveTaskId);
            progress.Stage = "reading";
            progress.StageTitle = "در حال بارگذاری و ذخیره موقت فایل در سرور...";
            progress.Percent = 5;

            Directory.CreateDirectory("Uploads");
            var tempPath = Path.Combine("Uploads", $"Stuffs_Upload_{Guid.NewGuid():N}.xlsx");

            try
            {
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    await ExcelFile.CopyToAsync(fileStream, ct);
                }

                progress.StageTitle = "فایل با موفقیت دریافت شد. در حال استخراج و تحلیل ساختار اکسل...";
                progress.Percent = 10;

                if (isAjax)
                {
                    var capturedTaskId = effectiveTaskId;
                    var capturedUpdateExisting = UpdateExisting;
                    var capturedFileName = ExcelFile.FileName;
                    var capturedUserName = User?.Identity?.Name ?? "Admin";

                    // ارسال به تسک پس‌زمینه با Scope مستقل جهت رهایی از محدودیت زمان اتصال HTTP
                    _ = Task.Run(async () =>
                    {
                        await ExecuteBackgroundImportAsync(tempPath, capturedTaskId, capturedUpdateExisting, capturedFileName, capturedUserName);
                    });

                    // بازگرداندن پاسخ فوری ۲۰۰ به کلاینت ظرف کمتر از ۱ ثانیه تا پروکسی و وب‌سرور هرگز ۵۰۲ یا ۵۰۴ ندهند
                    return new JsonResult(new
                    {
                        success = true,
                        started = true,
                        taskId = effectiveTaskId,
                        message = "فایل با موفقیت در سرور دریافت شد و پردازش غیرهمزمان در پس‌زمینه آغاز گردید."
                    });
                }

                // پردازش سنتی برای حالت بدون جاوااسکریپت (Fallback)
                var result = await ProcessLargeExcelFileAsync(_context, tempPath, effectiveTaskId, UpdateExisting, ct);
                InsertedCount = result.InsertedCount;
                UpdatedCount = result.UpdatedCount;
                ExpiredSkippedCount = result.ExpiredSkippedCount;
                SkippedCount = result.SkippedCount;
                TotalProcessedCount = result.TotalProcessed;

                await _activityLogger.LogAsync(
                    "شناسه کالا",
                    "بارگذاری اکسل شناسه کالاها",
                    $"بارگذاری موفقیت‌آمیز {InsertedCount:N0} شناسه جدید، بروزرسانی {UpdatedCount:N0} شناسه و نادیده گرفتن {ExpiredSkippedCount:N0} مورد منقضی شده از فایل {ExcelFile.FileName}",
                    entityId: InsertedCount.ToString(),
                    ct: ct);

                var resultDetails = new List<string>();
                resultDetails.Add($"{InsertedCount:N0} شناسه جدید افزوده شد");
                if (UpdatedCount > 0) resultDetails.Add($"{UpdatedCount:N0} مورد بروزرسانی گردید");
                if (ExpiredSkippedCount > 0) resultDetails.Add($"{ExpiredSkippedCount:N0} مورد منقضی‌شده نادیده گرفته شد");
                if (SkippedCount > 0) resultDetails.Add($"{SkippedCount:N0} مورد تکراری نادیده گرفته شد");

                SuccessMessage = "عملیات بارگذاری انبوه با موفقیت انجام شد: " + string.Join("، ", resultDetails) + ".";

                progress.Stage = "completed";
                progress.StageTitle = "عملیات بارگذاری با موفقیت به پایان رسید.";
                progress.Percent = 100;
                progress.IsCompleted = true;
                progress.InsertedCount = InsertedCount;
                progress.UpdatedCount = UpdatedCount;
                progress.ExpiredSkippedCount = ExpiredSkippedCount;
                progress.SkippedCount = SkippedCount;
                progress.ResultMessage = SuccessMessage;

                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش و بارگذاری فایل اکسل کالاها.");
                var errMsg = "خطا در پردازش فایل اکسل: " + ex.Message;

                progress.Stage = "error";
                progress.StageTitle = "خطا در فرآیند بارگذاری";
                progress.ErrorMessage = errMsg;
                progress.IsCompleted = true;

                try
                {
                    if (System.IO.File.Exists(tempPath))
                    {
                        System.IO.File.Delete(tempPath);
                    }
                }
                catch
                {
                }

                if (isAjax)
                {
                    return new JsonResult(new { success = false, message = errMsg });
                }

                ErrorMessage = errMsg;
                return Page();
            }
            finally
            {
                if (!isAjax)
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
                    }
                }
            }
        }

        private async Task ExecuteBackgroundImportAsync(string tempPath, string taskId, bool updateExisting, string fileName, string userName)
        {
            var progress = StuffImportProgressTracker.GetOrCreate(taskId);
            try
            {
                using var scope = _scopeFactory?.CreateScope();
                var scopedContext = scope != null
                    ? scope.ServiceProvider.GetRequiredService<DataContext>()
                    : _context;
                var scopedActivityLogger = scope != null
                    ? scope.ServiceProvider.GetRequiredService<IUserActivityLogger>()
                    : _activityLogger;
                var scopedLogger = scope != null
                    ? scope.ServiceProvider.GetRequiredService<ILogger<ImportModel>>()
                    : _logger;

                var result = await ProcessLargeExcelFileAsync(scopedContext, tempPath, taskId, updateExisting, CancellationToken.None);

                await scopedActivityLogger.LogAsync(
                    "شناسه کالا",
                    "بارگذاری اکسل شناسه کالاها",
                    $"بارگذاری موفقیت‌آمیز {result.InsertedCount:N0} شناسه جدید، بروزرسانی {result.UpdatedCount:N0} شناسه و نادیده گرفتن {result.ExpiredSkippedCount:N0} مورد منقضی شده از فایل {fileName}",
                    entityId: result.InsertedCount.ToString());

                var resultDetails = new List<string>();
                resultDetails.Add($"{result.InsertedCount:N0} شناسه جدید افزوده شد");
                if (result.UpdatedCount > 0) resultDetails.Add($"{result.UpdatedCount:N0} مورد بروزرسانی گردید");
                if (result.ExpiredSkippedCount > 0) resultDetails.Add($"{result.ExpiredSkippedCount:N0} مورد منقضی‌شده نادیده گرفته شد");
                if (result.SkippedCount > 0) resultDetails.Add($"{result.SkippedCount:N0} مورد تکراری نادیده گرفته شد");

                var successMsg = "عملیات بارگذاری انبوه با موفقیت انجام شد: " + string.Join("، ", resultDetails) + ".";

                progress.Stage = "completed";
                progress.StageTitle = "عملیات بارگذاری با موفقیت به پایان رسید.";
                progress.Percent = 100;
                progress.IsCompleted = true;
                progress.InsertedCount = result.InsertedCount;
                progress.UpdatedCount = result.UpdatedCount;
                progress.ExpiredSkippedCount = result.ExpiredSkippedCount;
                progress.SkippedCount = result.SkippedCount;
                progress.TotalEstimatedRows = result.TotalProcessed;
                progress.ProcessedRows = result.TotalProcessed;
                progress.ResultMessage = successMsg;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش پس‌زمینه فایل اکسل کالاها.");
                var errMsg = "خطا در پردازش فایل اکسل: " + ex.Message;
                progress.Stage = "error";
                progress.StageTitle = "خطا در فرآیند بارگذاری";
                progress.ErrorMessage = errMsg;
                progress.IsCompleted = true;
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
                }
            }
        }

        private async Task<StuffImportResult> ProcessLargeExcelFileAsync(DataContext context, string filePath, string taskId, bool updateExisting, CancellationToken ct)
        {
            var result = new StuffImportResult();

            using var document = SpreadsheetDocument.Open(filePath, false);
            var workbookPart = document.WorkbookPart;
            if (workbookPart == null)
                throw new InvalidOperationException("ساختار فایل اکسل نامعتبر است (WorkbookPart یافت نشد).");

            // 1. خواندن استریم جدول رشته‌های اشتراکی (SharedStrings) بدون مصرف بیهوده حافظه
            StuffImportProgressTracker.Update(taskId, p =>
            {
                p.StageTitle = "در حال خواندن جدول متن‌های اشتراکی...";
                p.Percent = 12;
            });

            var sharedStrings = new List<string>();
            if (workbookPart.SharedStringTablePart != null)
            {
                using var ssStream = workbookPart.SharedStringTablePart.GetStream(FileMode.Open, FileAccess.Read);
                using var ssReader = XmlReader.Create(ssStream, new XmlReaderSettings { IgnoreWhitespace = true });
                while (ssReader.Read())
                {
                    if (ssReader.NodeType == XmlNodeType.Element && ssReader.LocalName == "si")
                    {
                        using var sub = ssReader.ReadSubtree();
                        var sb = new StringBuilder();
                        while (sub.Read())
                        {
                            if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                            {
                                sb.Append(sub.ReadElementContentAsString());
                            }
                        }
                        sharedStrings.Add(sb.ToString());
                    }
                }
            }

            // 2. یافتن برگه نخست (Worksheet)
            var sheet = workbookPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().FirstOrDefault();
            if (sheet?.Id?.Value == null)
                throw new InvalidOperationException("برگه‌ای در فایل اکسل یافت نشد.");

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);

            // 3. باز کردن جریان داده‌های برگه با XmlReader (Zero-DOM Streaming)
            using var sheetStream = worksheetPart.GetStream(FileMode.Open, FileAccess.Read);
            using var xmlReader = XmlReader.Create(sheetStream, new XmlReaderSettings { IgnoreWhitespace = true });

            int totalEstimatedRows = 0;
            int codeCol = -1;
            int titleCol = -1;
            int vatRateCol = -1;
            int vatTaxCol = -1;
            int expireCol = -1;
            int typeCol = -1;
            int specialCol = -1;
            int customRateCol = -1;

            bool isFirstRow = true;
            var today = DateTime.Today;

            var rowBuffer = new string?[64];
            int maxColIndexSeen = 0;

            // تشخیص تعداد کل سطرها از تگ dimension (در صورت وجود)
            while (xmlReader.Read())
            {
                if (xmlReader.NodeType == XmlNodeType.Element)
                {
                    if (xmlReader.LocalName == "dimension")
                    {
                        var dimRef = xmlReader.GetAttribute("ref");
                        if (!string.IsNullOrWhiteSpace(dimRef) && dimRef.Contains(':'))
                        {
                            var parts = dimRef.Split(':');
                            if (parts.Length > 1)
                            {
                                var digits = new string(parts[1].Where(char.IsDigit).ToArray());
                                if (int.TryParse(digits, out int estRows))
                                {
                                    totalEstimatedRows = Math.Max(0, estRows - 1);
                                    StuffImportProgressTracker.Update(taskId, p =>
                                    {
                                        p.TotalEstimatedRows = totalEstimatedRows;
                                    });
                                }
                            }
                        }
                    }
                    else if (xmlReader.LocalName == "sheetData")
                    {
                        break;
                    }
                }
            }

            StuffImportProgressTracker.Update(taskId, p =>
            {
                p.Stage = "saving";
                p.StageTitle = "در حال خواندن و استخراج ردیف‌های فایل اکسل...";
                p.Percent = 15;
            });

            // آماده‌سازی برای ذخیره فوق‌سریع در دیتابیس
            var isSqlServer = context.Database.IsSqlServer();
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            var sqlConnection = isSqlServer ? (connection as SqlConnection) : null;

            // ایجاد جدول موقت دسته‌ای و پاکسازی تراکنش‌های معلق با CHECKPOINT
            if (sqlConnection != null)
            {
                using var createTempCmd = sqlConnection.CreateCommand();
                createTempCmd.CommandText = @"
CHECKPOINT;
IF OBJECT_ID('tempdb..#StagingBatch') IS NOT NULL DROP TABLE #StagingBatch;
CREATE TABLE #StagingBatch (
    [Code] NVARCHAR(100) NOT NULL,
    [Title] NVARCHAR(MAX) NULL,
    [Type] INT NOT NULL,
    [Special] BIT NOT NULL,
    [VatTax] BIT NOT NULL,
    [VatRate] INT NOT NULL,
    [VatCustomRate] INT NOT NULL
);";
                createTempCmd.CommandTimeout = 120;
                await createTempCmd.ExecuteNonQueryAsync(ct);
            }

            var bulkTable = new DataTable();
            bulkTable.Columns.Add("Code", typeof(string));
            bulkTable.Columns.Add("Title", typeof(string));
            bulkTable.Columns.Add("Type", typeof(int));
            bulkTable.Columns.Add("Special", typeof(bool));
            bulkTable.Columns.Add("VatTax", typeof(bool));
            bulkTable.Columns.Add("VatRate", typeof(int));
            bulkTable.Columns.Add("VatCustomRate", typeof(int));

            var sqliteFallbackBatch = new List<Stuff>();

            int processedRowCount = 0;
            int lastProgressReportedRow = 0;

            // خواندن سطر به سطر فایل XML بدون ساخت درخت در حافظه
            while (xmlReader.Read())
            {
                if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.LocalName == "row")
                {
                    Array.Clear(rowBuffer, 0, rowBuffer.Length);
                    maxColIndexSeen = 0;

                    using var rowReader = xmlReader.ReadSubtree();
                    while (rowReader.Read())
                    {
                        if (rowReader.NodeType == XmlNodeType.Element && rowReader.LocalName == "c")
                        {
                            var cellRef = rowReader.GetAttribute("r");
                            var cellType = rowReader.GetAttribute("t");
                            int colIndex = CellRefToColIndex(cellRef);

                            if (colIndex >= 0 && colIndex < rowBuffer.Length)
                            {
                                if (colIndex > maxColIndexSeen) maxColIndexSeen = colIndex;

                                using var cellReader = rowReader.ReadSubtree();
                                string? rawVal = null;
                                while (cellReader.Read())
                                {
                                    if (cellReader.NodeType == XmlNodeType.Element && (cellReader.LocalName == "v" || cellReader.LocalName == "t"))
                                    {
                                        rawVal = cellReader.ReadElementContentAsString();
                                    }
                                }

                                if (cellType == "s" && int.TryParse(rawVal, out int ssIdx) && ssIdx >= 0 && ssIdx < sharedStrings.Count)
                                {
                                    rowBuffer[colIndex] = sharedStrings[ssIdx];
                                }
                                else
                                {
                                    rowBuffer[colIndex] = rawVal;
                                }
                            }
                        }
                    }

                    // سطر اول: تشخیص ستون‌ها از روی هدر
                    if (isFirstRow)
                    {
                        isFirstRow = false;
                        for (int i = 0; i <= maxColIndexSeen; i++)
                        {
                            var h = (rowBuffer[i] ?? "").Trim().ToLowerInvariant();
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

                        if (codeCol < 0) codeCol = 0;
                        if (titleCol < 0) titleCol = 1;
                        if (vatRateCol < 0) vatRateCol = 2;
                        if (vatTaxCol < 0) vatTaxCol = 3;
                        if (typeCol < 0) typeCol = maxColIndexSeen >= 8 ? 8 : 3;

                        continue;
                    }

                    // سطرهای داده
                    processedRowCount++;

                    // بررسی انقضا
                    if (expireCol >= 0 && expireCol <= maxColIndexSeen)
                    {
                        var rawExpire = rowBuffer[expireCol];
                        if (IsExpired(rawExpire, today))
                        {
                            result.ExpiredSkippedCount++;
                            continue;
                        }
                    }

                    string? rawCode = codeCol >= 0 && codeCol <= maxColIndexSeen ? rowBuffer[codeCol] : null;
                    string? rawTitle = titleCol >= 0 && titleCol <= maxColIndexSeen ? rowBuffer[titleCol] : null;

                    var cleanCode = NormalizeStuffCode(rawCode);
                    if (string.IsNullOrWhiteSpace(cleanCode) || string.IsNullOrWhiteSpace(rawTitle))
                        continue;

                    var cleanTitle = rawTitle.Trim();

                    int vatRate = 0;
                    if (vatRateCol >= 0 && vatRateCol <= maxColIndexSeen)
                    {
                        vatRate = ParseVatRate(rowBuffer[vatRateCol]);
                    }

                    StuffType stuffType = StuffType.شناسه_عمومی_تولید_داخل_کالا;
                    if (typeCol >= 0 && typeCol <= maxColIndexSeen)
                    {
                        stuffType = ParseStuffType(rowBuffer[typeCol]);
                    }

                    bool vatTax = true;
                    if (vatTaxCol >= 0 && vatTaxCol <= maxColIndexSeen)
                    {
                        vatTax = ParseTaxable(rowBuffer[vatTaxCol]);
                    }

                    bool isSpecial = false;
                    if (specialCol >= 0 && specialCol <= maxColIndexSeen)
                    {
                        var rawSpecial = (rowBuffer[specialCol] ?? "").Trim().ToLowerInvariant();
                        if (rawSpecial == "بله" || rawSpecial == "دارد" || rawSpecial == "1" || rawSpecial == "true")
                        {
                            isSpecial = true;
                        }
                    }

                    int vatCustomRate = 0;
                    if (customRateCol >= 0 && customRateCol <= maxColIndexSeen)
                    {
                        vatCustomRate = ParseVatRate(rowBuffer[customRateCol]);
                    }

                    // درج در بچ متناظر
                    if (sqlConnection != null)
                    {
                        bulkTable.Rows.Add(cleanCode, cleanTitle, (int)stuffType, isSpecial, vatTax, vatRate, vatCustomRate);

                        // هر ۵۰۰۰ رکورد، عملیات درج و ادغام دسته‌ای و بازیافت خودکار لاگ انجام می‌شود
                        if (bulkTable.Rows.Count >= 5000)
                        {
                            await FlushAndMergeBatchToSqlServerAsync(sqlConnection, bulkTable, updateExisting, result, ct);
                            bulkTable.Clear();

                            // بروزرسانی پیشرفت
                            UpdateImportProgress(taskId, processedRowCount, totalEstimatedRows, result);
                            lastProgressReportedRow = processedRowCount;
                        }
                    }
                    else
                    {
                        // حالت Fallback برای SQLite / تست‌های واحد
                        sqliteFallbackBatch.Add(new Stuff
                        {
                            Code = cleanCode,
                            Title = cleanTitle,
                            Type = stuffType,
                            Special = isSpecial,
                            VatTax = vatTax,
                            VatRate = vatRate,
                            VatCustomRate = vatCustomRate
                        });

                        if (sqliteFallbackBatch.Count >= 250)
                        {
                            await FlushBatchToSqliteAsync(context, sqliteFallbackBatch, updateExisting, result, ct);
                            sqliteFallbackBatch.Clear();
                            UpdateImportProgress(taskId, processedRowCount, totalEstimatedRows, result);
                            lastProgressReportedRow = processedRowCount;
                        }
                    }

                    // بروزرسانی نوار پیشرفت هر ۵۰۰۰ سطر
                    if (processedRowCount - lastProgressReportedRow >= 5000)
                    {
                        UpdateImportProgress(taskId, processedRowCount, totalEstimatedRows, result);
                        lastProgressReportedRow = processedRowCount;
                    }
                }
            }

            // ارسال باقیمانده داده‌ها
            if (sqlConnection != null && bulkTable.Rows.Count > 0)
            {
                await FlushAndMergeBatchToSqlServerAsync(sqlConnection, bulkTable, updateExisting, result, ct);
                bulkTable.Clear();
            }
            else if (sqliteFallbackBatch.Count > 0)
            {
                await FlushBatchToSqliteAsync(context, sqliteFallbackBatch, updateExisting, result, ct);
                sqliteFallbackBatch.Clear();
            }

            // پاکسازی جدول موقت و آزاد‌سازی حافظه
            if (sqlConnection != null)
            {
                using var cleanupCmd = sqlConnection.CreateCommand();
                cleanupCmd.CommandText = "IF OBJECT_ID('tempdb..#StagingBatch') IS NOT NULL DROP TABLE #StagingBatch; CHECKPOINT;";
                await cleanupCmd.ExecuteNonQueryAsync(ct);
            }

            result.TotalProcessed = processedRowCount;
            StuffImportProgressTracker.Update(taskId, p =>
            {
                p.ProcessedRows = processedRowCount;
                p.InsertedCount = result.InsertedCount;
                p.UpdatedCount = result.UpdatedCount;
                p.ExpiredSkippedCount = result.ExpiredSkippedCount;
                p.SkippedCount = result.SkippedCount;
                p.Percent = 100;
                p.StageTitle = "عملیات استخراج و درج با موفقیت به پایان رسید.";
            });

            return result;
        }

        private async Task FlushAndMergeBatchToSqlServerAsync(SqlConnection sqlConnection, DataTable table, bool updateExisting, StuffImportResult result, CancellationToken ct)
        {
            using (var bulkCopy = new SqlBulkCopy(sqlConnection, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = "#StagingBatch",
                BatchSize = table.Rows.Count,
                BulkCopyTimeout = 180
            })
            {
                bulkCopy.ColumnMappings.Add("Code", "Code");
                bulkCopy.ColumnMappings.Add("Title", "Title");
                bulkCopy.ColumnMappings.Add("Type", "Type");
                bulkCopy.ColumnMappings.Add("Special", "Special");
                bulkCopy.ColumnMappings.Add("VatTax", "VatTax");
                bulkCopy.ColumnMappings.Add("VatRate", "VatRate");
                bulkCopy.ColumnMappings.Add("VatCustomRate", "VatCustomRate");

                await bulkCopy.WriteToServerAsync(table, ct);
            }

            using var cmd = sqlConnection.CreateCommand();
            cmd.CommandTimeout = 180;
            cmd.CommandText = @"
SET ANSI_WARNINGS OFF;
DECLARE @updateExisting BIT = " + (updateExisting ? "1" : "0") + @";
DECLARE @NewRows INT, @ExistingRows INT;

SELECT
    @NewRows = COUNT(CASE WHEN t.[Code] IS NULL THEN 1 END),
    @ExistingRows = COUNT(CASE WHEN t.[Code] IS NOT NULL THEN 1 END)
FROM (
    SELECT [Code], ROW_NUMBER() OVER(PARTITION BY [Code] ORDER BY (SELECT NULL)) AS rn
    FROM #StagingBatch
) b
LEFT JOIN [Stuffs] t ON b.[Code] = t.[Code]
WHERE b.rn = 1;

IF @updateExisting = 1 AND @ExistingRows > 0
BEGIN
    UPDATE t
    SET t.[Title] = b.[Title],
        t.[Type] = b.[Type],
        t.[Special] = b.[Special],
        t.[VatTax] = b.[VatTax],
        t.[VatRate] = b.[VatRate],
        t.[VatCustomRate] = b.[VatCustomRate]
    FROM [Stuffs] t
    INNER JOIN (
        SELECT [Code], [Title], [Type], [Special], [VatTax], [VatRate], [VatCustomRate],
               ROW_NUMBER() OVER(PARTITION BY [Code] ORDER BY (SELECT NULL)) AS rn
        FROM #StagingBatch
    ) b ON t.[Code] = b.[Code] AND b.rn = 1;
END

IF @NewRows > 0
BEGIN
    INSERT INTO [Stuffs] ([Code], [Title], [Type], [Special], [VatTax], [VatRate], [VatCustomRate])
    SELECT b.[Code], b.[Title], b.[Type], b.[Special], b.[VatTax], b.[VatRate], b.[VatCustomRate]
    FROM (
        SELECT [Code], [Title], [Type], [Special], [VatTax], [VatRate], [VatCustomRate],
               ROW_NUMBER() OVER(PARTITION BY [Code] ORDER BY (SELECT NULL)) AS rn
        FROM #StagingBatch
    ) b
    LEFT JOIN [Stuffs] t ON b.[Code] = t.[Code]
    WHERE t.[Code] IS NULL AND b.rn = 1;
END

SELECT @NewRows AS NewRows, @ExistingRows AS ExistingRows;

TRUNCATE TABLE #StagingBatch;
CHECKPOINT;
";

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                int newRows = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                int existingRows = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));

                result.InsertedCount += newRows;
                if (updateExisting)
                {
                    result.UpdatedCount += existingRows;
                }
                else
                {
                    result.SkippedCount += existingRows;
                }
            }
        }

        private async Task FlushBatchToSqliteAsync(DataContext context, List<Stuff> batch, bool updateExisting, StuffImportResult result, CancellationToken ct)
        {
            var chunkCodes = batch.Select(c => c.Code).ToList();
            var existingBatch = await context.Stuffs
                .Where(s => chunkCodes.Contains(s.Code))
                .ToDictionaryAsync(s => s.Code, StringComparer.OrdinalIgnoreCase, ct);

            foreach (var item in batch)
            {
                if (existingBatch.TryGetValue(item.Code, out var existing))
                {
                    if (updateExisting)
                    {
                        existing.Title = item.Title;
                        existing.VatRate = item.VatRate;
                        existing.Type = item.Type;
                        existing.VatTax = item.VatTax;
                        existing.Special = item.Special;
                        existing.VatCustomRate = item.VatCustomRate;
                        result.UpdatedCount++;
                    }
                    else
                    {
                        result.SkippedCount++;
                    }
                }
                else
                {
                    context.Stuffs.Add(item);
                    result.InsertedCount++;
                }
            }

            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        private void UpdateImportProgress(string taskId, int processedRows, int totalEstimatedRows, StuffImportResult result)
        {
            StuffImportProgressTracker.Update(taskId, p =>
            {
                p.ProcessedRows = processedRows;
                p.InsertedCount = result.InsertedCount;
                p.UpdatedCount = result.UpdatedCount;
                p.ExpiredSkippedCount = result.ExpiredSkippedCount;
                p.SkippedCount = result.SkippedCount;

                if (totalEstimatedRows > 0)
                {
                    var calc = (int)((processedRows / (double)totalEstimatedRows) * 70) + 15;
                    p.Percent = Math.Clamp(calc, 15, 85);
                    p.StageTitle = $"در حال استخراج و درج دسته‌ای شناسه‌ها (سطر {processedRows:N0} از حدود {totalEstimatedRows:N0})...";
                }
                else
                {
                    p.Percent = Math.Min(80, 15 + (processedRows / 10000));
                    p.StageTitle = $"در حال استخراج و درج دسته‌ای شناسه‌ها ({processedRows:N0} رکورد پردازش شد)...";
                }
            });
        }

        public static int CellRefToColIndex(string? cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return -1;
            int col = 0;
            bool foundLetter = false;
            for (int i = 0; i < cellRef.Length; i++)
            {
                char c = cellRef[i];
                if (c >= 'A' && c <= 'Z')
                {
                    col = (col * 26) + (c - 'A' + 1);
                    foundLetter = true;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    col = (col * 26) + (c - 'a' + 1);
                    foundLetter = true;
                }
                else
                {
                    break;
                }
            }
            return foundLetter ? (col - 1) : -1;
        }

        public static string? NormalizeStuffCode(string? rawCode)
        {
            if (string.IsNullOrWhiteSpace(rawCode)) return null;

            var clean = rawCode.Trim();
            clean = Utils.ConvertPersianNumberToEnglish(clean);
            clean = clean.Replace(" ", "").Replace("-", "").Replace("_", "");

            if (string.IsNullOrWhiteSpace(clean)) return null;

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
                    }
                }
            }

            if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double oaDate) && oaDate > 30000 && oaDate < 100000)
            {
                try
                {
                    return DateTime.FromOADate(oaDate).Date < today;
                }
                catch
                {
                }
            }

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
