using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Invoice.Code;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.Admin.Bale
{
    [Authorize(Roles = "admin")]
    public class IndexModel : PageModel
    {
        private readonly IBaleSettingsService _settingsService;
        private readonly IBaleNotificationService _notificationService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            IBaleSettingsService settingsService,
            IBaleNotificationService notificationService,
            ILogger<IndexModel> logger)
        {
            _settingsService = settingsService;
            _notificationService = notificationService;
            _logger = logger;
        }

        [BindProperty]
        public BaleSettings Settings { get; set; } = new();

        public BaleBotInfo? BotInfo { get; set; }

        public List<BaleChatSummary> RecentChats { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            Settings = await _settingsService.GetSettingsAsync();

            if (!string.IsNullOrWhiteSpace(Settings.BotToken))
            {
                BotInfo = await _settingsService.TestBotTokenAsync(Settings.BotToken);
            }
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            try
            {
                var existing = await _settingsService.GetSettingsAsync();

                // اگر فیلد توکن خالی ارسال شده باشد، توکن قبلی حفظ می‌شود تا با تغییر سایر فیلدها نپرد
                if (string.IsNullOrWhiteSpace(Settings.BotToken))
                {
                    Settings.BotToken = existing.BotToken ?? string.Empty;
                }
                else
                {
                    Settings.BotToken = Settings.BotToken.Trim();
                }

                Settings.ChatId = Settings.ChatId?.Trim() ?? string.Empty;
                Settings.BaseUrl = Settings.BaseUrl?.Trim() ?? string.Empty;
                Settings.CustomTitle = string.IsNullOrWhiteSpace(Settings.CustomTitle) ? "سامانه صدور فاکتور" : Settings.CustomTitle.Trim();

                await _settingsService.SaveSettingsAsync(Settings);
                StatusMessage = "تنظیمات پیام‌رسان بله با موفقیت ذخیره شد.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ذخیره تنظیمات بله");
                ErrorMessage = $"خطا در ذخیره تنظیمات: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostClearTokenAsync()
        {
            try
            {
                var existing = await _settingsService.GetSettingsAsync();
                existing.BotToken = string.Empty;
                await _settingsService.SaveSettingsAsync(existing);
                StatusMessage = "توکن ربات با موفقیت حذف گردید.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در حذف توکن ربات بله");
                ErrorMessage = $"خطا در حذف توکن: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostTestTokenAsync()
        {
            var saved = await _settingsService.GetSettingsAsync();
            var token = Settings?.BotToken?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                token = saved.BotToken?.Trim();
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                ErrorMessage = "لطفاً ابتدا توکن ربات بله را وارد نمایید.";
                Settings = saved;
                return Page();
            }

            var info = await _settingsService.TestBotTokenAsync(token);
            BotInfo = info;
            if (info.Success)
            {
                StatusMessage = $"توکن معتبر است. مشخصات ربات: {info.FirstName} (شناسه کاربری: @{info.Username} - شناسه عددی: {info.Id})";
                Settings.BotToken = token;
            }
            else
            {
                ErrorMessage = $"خطا در بررسی توکن ربات: {info.ErrorMessage}";
                Settings.BotToken = token;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostSendTestMessageAsync()
        {
            var saved = await _settingsService.GetSettingsAsync();
            var targetChatId = !string.IsNullOrWhiteSpace(Settings?.ChatId) ? Settings.ChatId.Trim() : saved.ChatId?.Trim();

            if (string.IsNullOrWhiteSpace(targetChatId))
            {
                ErrorMessage = "لطفاً شناسه چت (Chat ID) را جهت ارسال پیام تست وارد نمایید.";
                return RedirectToPage();
            }

            // اگر تنظیمات فرم هنوز ذخیره نشده بود، با تنظیمات جاری ذخیره می‌کنیم
            var effectiveToken = !string.IsNullOrWhiteSpace(Settings?.BotToken) ? Settings.BotToken.Trim() : saved.BotToken?.Trim();
            if (!string.IsNullOrWhiteSpace(effectiveToken) && effectiveToken != saved.BotToken)
            {
                saved.BotToken = effectiveToken;
                saved.ChatId = targetChatId;
                await _settingsService.SaveSettingsAsync(saved);
            }

            var sent = await _notificationService.SendTestNotificationAsync(targetChatId);
            if (sent)
            {
                StatusMessage = $"پیام آزمایشی با موفقیت به چت {targetChatId} در پیام‌رسان بله ارسال شد.";
            }
            else
            {
                ErrorMessage = "ارسال پیام تست با خطا مواجه شد. لطفاً توکن ربات و شناسه چت را بررسی نمایید.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostGetChatsAsync()
        {
            var saved = await _settingsService.GetSettingsAsync();
            var token = !string.IsNullOrWhiteSpace(Settings?.BotToken) ? Settings.BotToken.Trim() : saved.BotToken?.Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                ErrorMessage = "برای دریافت آخرین پیام‌ها ابتدا توکن ربات را وارد و ذخیره کنید.";
                Settings = saved;
                return Page();
            }

            RecentChats = await _settingsService.GetRecentChatsAsync(token);
            if (RecentChats.Count == 0)
            {
                StatusMessage = "هیچ پیام جدیدی یافت نشد. لطفاً در پیام‌رسان بله به ربات خود پیام start/ بفرستید و مجدداً امتحان کنید.";
            }

            BotInfo = await _settingsService.TestBotTokenAsync(token);
            Settings.BotToken = token;
            return Page();
        }
    }
}
