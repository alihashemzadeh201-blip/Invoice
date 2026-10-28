using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Invoice.Code;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.Admin.Bale
{
    [Authorize(Roles = "admin")]
    public class SendMessageModel : PageModel
    {
        private readonly IBaleUserBotService _userBotService;
        private readonly IBaleSettingsService _settingsService;
        private readonly ILogger<SendMessageModel> _logger;

        public SendMessageModel(
            IBaleUserBotService userBotService,
            IBaleSettingsService settingsService,
            ILogger<SendMessageModel> logger)
        {
            _userBotService = userBotService;
            _settingsService = settingsService;
            _logger = logger;
        }

        public List<BaleUserInfo> Users { get; set; } = new();

        [BindProperty]
        public string? MessageText { get; set; }

        [BindProperty]
        public List<string> SelectedChatIds { get; set; } = new();

        [BindProperty]
        public string? DirectChatId { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public bool IsUserBotConfigured { get; set; }

        public async Task OnGetAsync()
        {
            var settings = await _settingsService.GetSettingsAsync();
            IsUserBotConfigured = !string.IsNullOrWhiteSpace(settings.UserBotToken);
            Users = await _userBotService.GetBaleUsersListAsync();
        }

        public async Task<IActionResult> OnPostSendSelectedAsync()
        {
            if (string.IsNullOrWhiteSpace(MessageText))
            {
                ErrorMessage = "لطفاً متن پیام را وارد نمایید.";
                return RedirectToPage();
            }

            var targets = SelectedChatIds != null
                ? SelectedChatIds.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList()
                : new List<string>();

            if (targets.Count == 0)
            {
                ErrorMessage = "هیچ کاربری برای دریافت پیام انتخاب نشده است. لطفاً چک‌باکس حداقل یک کاربر را انتخاب نمایید.";
                return RedirectToPage();
            }

            try
            {
                var count = await _userBotService.SendMessageToSelectedUsersAsync(MessageText.Trim(), targets);
                if (count > 0)
                {
                    StatusMessage = $"پیام با موفقیت به {count} کاربر از {targets.Count} کاربر انتخاب‌شده ارسال شد.";
                }
                else
                {
                    ErrorMessage = "ارسال پیام به کاربران انتخاب‌شده با خطا مواجه شد. لطفاً توکن ربات کاربر در بخش تنظیمات بله را بررسی فرمایید.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال پیام به کاربران انتخاب‌شده در بله");
                ErrorMessage = $"خطا در فرآیند ارسال پیام: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSendToAllAsync()
        {
            if (string.IsNullOrWhiteSpace(MessageText))
            {
                ErrorMessage = "لطفاً متن پیام را وارد نمایید.";
                return RedirectToPage();
            }

            try
            {
                var count = await _userBotService.BroadcastMessageAsync(MessageText.Trim(), null);
                if (count > 0)
                {
                    StatusMessage = $"پیام همگانی با موفقیت به {count} مخاطب در بله ارسال گردید.";
                }
                else
                {
                    ErrorMessage = "هیچ کاربری جهت دریافت پیام یافت نشد یا ارسال با خطا مواجه گردید.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال پیام همگانی در بله");
                ErrorMessage = $"خطا در فرآیند ارسال: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSendDirectAsync()
        {
            if (string.IsNullOrWhiteSpace(DirectChatId))
            {
                ErrorMessage = "شناسه چت مقصد نمی‌تواند خالی باشد.";
                return RedirectToPage();
            }

            if (string.IsNullOrWhiteSpace(MessageText))
            {
                ErrorMessage = "متن پیام نمی‌تواند خالی باشد.";
                return RedirectToPage();
            }

            try
            {
                var ok = await _userBotService.SendUserMessageAsync(DirectChatId.Trim(), MessageText.Trim());
                if (ok)
                {
                    StatusMessage = $"پیام با موفقیت به شناسه چت {DirectChatId} ارسال شد.";
                }
                else
                {
                    ErrorMessage = $"ارسال پیام به شناسه چت {DirectChatId} ناموفق بود. لطفاً از فعال بودن ربات کاربر و صحت Chat ID اطمینان حاصل نمایید.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال پیام تکی به چت {ChatId}", DirectChatId);
                ErrorMessage = $"خطا در ارسال پیام: {ex.Message}";
            }

            return RedirectToPage();
        }
    }
}
