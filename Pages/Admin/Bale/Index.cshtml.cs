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
        private readonly IBaleUserBotService? _userBotService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            IBaleSettingsService settingsService,
            IBaleNotificationService notificationService,
            ILogger<IndexModel> logger,
            IBaleUserBotService? userBotService = null)
        {
            _settingsService = settingsService;
            _notificationService = notificationService;
            _logger = logger;
            _userBotService = userBotService;
        }

        [BindProperty]
        public BaleSettings Settings { get; set; } = new();

        public BaleBotInfo? BotInfo { get; set; }

        public BaleBotInfo? UserBotInfo { get; set; }

        public List<BaleChatSummary> RecentChats { get; set; } = new();

        public int TotalBaleUsersCount { get; set; }

        public List<string> AvailableUserChatIds { get; set; } = new();

        public string SuggestedUserWebhookUrl
        {
            get
            {
                var baseUri = !string.IsNullOrWhiteSpace(Settings.BaseUrl)
                    ? Settings.BaseUrl.TrimEnd('/')
                    : $"{Request.Scheme}://{Request.Host}";
                return $"{baseUri}/api/bale/user-webhook";
            }
        }

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

            if (!string.IsNullOrWhiteSpace(Settings.UserBotToken))
            {
                UserBotInfo = await _settingsService.TestBotTokenAsync(Settings.UserBotToken);
            }

            if (_userBotService != null)
            {
                AvailableUserChatIds = await _userBotService.GetAllUserChatIdsAsync();
                TotalBaleUsersCount = AvailableUserChatIds.Count;
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

                if (string.IsNullOrWhiteSpace(Settings.UserBotToken))
                {
                    Settings.UserBotToken = existing.UserBotToken ?? string.Empty;
                }
                else
                {
                    Settings.UserBotToken = Settings.UserBotToken.Trim();
                }

                if (string.IsNullOrWhiteSpace(Settings.UserBotPaymentProviderToken))
                {
                    Settings.UserBotPaymentProviderToken = existing.UserBotPaymentProviderToken ?? string.Empty;
                }
                else
                {
                    Settings.UserBotPaymentProviderToken = Settings.UserBotPaymentProviderToken.Trim();
                }

                Settings.ChatId = Settings.ChatId?.Trim() ?? string.Empty;
                Settings.BaseUrl = Settings.BaseUrl?.Trim() ?? string.Empty;
                Settings.CustomTitle = string.IsNullOrWhiteSpace(Settings.CustomTitle) ? "سامانه صدور فاکتور" : Settings.CustomTitle.Trim();
                Settings.UpdateMode = string.Equals(Settings.UpdateMode, "Webhook", StringComparison.OrdinalIgnoreCase) ? "Webhook" : "Polling";
                Settings.AdminChatIds = Settings.AdminChatIds?.Trim() ?? string.Empty;
                Settings.WebhookSecretToken = Settings.WebhookSecretToken?.Trim() ?? string.Empty;
                Settings.UserBotWebhookSecret = Settings.UserBotWebhookSecret?.Trim() ?? string.Empty;

                if (Settings.RegistrationFee <= 0)
                {
                    Settings.RegistrationFee = 5000000;
                }

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

        public async Task<IActionResult> OnPostClearUserBotTokenAsync()
        {
            try
            {
                var existing = await _settingsService.GetSettingsAsync();
                existing.UserBotToken = string.Empty;
                await _settingsService.SaveSettingsAsync(existing);
                StatusMessage = "توکن ربات کاربر با موفقیت حذف گردید.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در حذف توکن ربات کاربر بله");
                ErrorMessage = $"خطا در حذف توکن ربات کاربر: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostTestUserBotTokenAsync()
        {
            var saved = await _settingsService.GetSettingsAsync();
            var token = Settings?.UserBotToken?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                token = saved.UserBotToken?.Trim();
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                ErrorMessage = "لطفاً ابتدا توکن ربات کاربر بله را وارد نمایید.";
                Settings = saved;
                return Page();
            }

            var info = await _settingsService.TestBotTokenAsync(token);
            UserBotInfo = info;
            if (info.Success)
            {
                StatusMessage = $"توکن ربات کاربر معتبر است. مشخصات: {info.FirstName} (شناسه: @{info.Username} - شناسه عددی: {info.Id})";
                Settings.UserBotToken = token;
            }
            else
            {
                ErrorMessage = $"خطا در بررسی توکن ربات کاربر: {info.ErrorMessage}";
                Settings.UserBotToken = token;
            }

            return Page();
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

        public string SuggestedWebhookUrl
        {
            get
            {
                var baseU = Settings?.BaseUrl?.Trim();
                if (string.IsNullOrEmpty(baseU))
                {
                    try
                    {
                        baseU = $"{Request.Scheme}://{Request.Host.Value}".TrimEnd('/');
                    }
                    catch
                    {
                        baseU = "https://yourdomain.com";
                    }
                }
                return $"{baseU.TrimEnd('/')}/api/bale/webhook";
            }
        }

        public async Task<IActionResult> OnPostSetWebhookAsync()
        {
            var saved = await _settingsService.GetSettingsAsync();
            var effectiveToken = !string.IsNullOrWhiteSpace(Settings?.BotToken) ? Settings.BotToken.Trim() : saved.BotToken?.Trim();
            if (string.IsNullOrWhiteSpace(effectiveToken))
            {
                ErrorMessage = "لطفاً ابتدا توکن ربات بله را وارد و ذخیره کنید.";
                return RedirectToPage();
            }

            var baseUrl = !string.IsNullOrWhiteSpace(Settings?.BaseUrl) ? Settings.BaseUrl.Trim() : saved.BaseUrl?.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = $"{Request.Scheme}://{Request.Host.Value}".TrimEnd('/');
            }

            if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "برای ثبت وب‌هوک، آدرس دامنه سامانه (Base URL) حتماً باید با پروتکل امن https آغاز شود.";
                return RedirectToPage();
            }

            var webhookUrl = $"{baseUrl.TrimEnd('/')}/api/bale/webhook";
            var secretToken = !string.IsNullOrWhiteSpace(Settings?.WebhookSecretToken)
                ? Settings.WebhookSecretToken.Trim()
                : saved.WebhookSecretToken?.Trim();

            var success = await _settingsService.SetWebhookAsync(webhookUrl, secretToken);
            if (success)
            {
                saved.UpdateMode = "Webhook";
                saved.BaseUrl = baseUrl;
                if (!string.IsNullOrWhiteSpace(secretToken)) saved.WebhookSecretToken = secretToken;
                await _settingsService.SaveSettingsAsync(saved);
                StatusMessage = $"وب‌هوک ربات با موفقیت در آدرس {webhookUrl} ثبت شد و حالت دریافت به Webhook تغییر یافت.";
            }
            else
            {
                ErrorMessage = "ثبت وب‌هوک با خطا مواجه شد. لطفاً توکن ربات و در دسترس بودن دامنه HTTPS را بررسی کنید.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteWebhookAsync()
        {
            var saved = await _settingsService.GetSettingsAsync();
            var success = await _settingsService.DeleteWebhookAsync();
            if (success)
            {
                saved.UpdateMode = "Polling";
                await _settingsService.SaveSettingsAsync(saved);
                StatusMessage = "وب‌هوک با موفقیت حذف گردید و حالت دریافت به خودکار (Polling) بازگردانده شد.";
            }
            else
            {
                ErrorMessage = "حذف وب‌هوک با خطا مواجه شد.";
            }
            return RedirectToPage();
        }
    }
}
