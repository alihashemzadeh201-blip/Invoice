using System;
using System.Threading.Tasks;
using Invoice.Code;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.Admin.DatabaseBackup
{
    [Authorize(Roles = "admin")]
    public class IndexModel : PageModel
    {
        private readonly IDatabaseBackupService _backupService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(IDatabaseBackupService backupService, ILogger<IndexModel> logger)
        {
            _backupService = backupService;
            _logger = logger;
        }

        public DatabaseInfo DbInfo { get; set; } = new DatabaseInfo();

        [BindProperty]
        public string BackupFormat { get; set; } = "zip";

        [BindProperty]
        public bool UseCompression { get; set; } = true;

        [TempData]
        public string? StatusMessage { get; set; }

        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                DbInfo = await _backupService.GetDatabaseInfoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در دریافت اطلاعات دیتابیس.");
                ErrorMessage = "امکان دریافت اطلاعات اولیه پایگاه داده وجود ندارد: " + ex.Message;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostDownloadAsync()
        {
            try
            {
                _logger.LogInformation("درخواست پشتیبان‌گیری دیتابیس با فرمت {Format} توسط مدیر {Admin}", BackupFormat, User.Identity?.Name);
                var result = await _backupService.CreateBackupStreamAsync(BackupFormat, UseCompression, HttpContext.RequestAborted);

                // بازگرداندن فایل پشتیبان جهت دانلود در مرورگر
                return File(result.Stream, result.ContentType, result.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در فرآیند پشتیبان‌گیری و دانلود پایگاه داده.");
                ErrorMessage = "خطا در تهیه فایل پشتیبان: " + ex.Message;

                try
                {
                    DbInfo = await _backupService.GetDatabaseInfoAsync();
                }
                catch
                {
                    // در صورت بروز خطای مجدد نادیده گرفته می‌شود
                }

                return Page();
            }
        }
    }
}
