using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public interface IBaleNotificationService
    {
        Task<bool> SendMessageAsync(string text, string? chatId = null, string parseMode = "HTML", CancellationToken ct = default);
        Task<bool> SendTestNotificationAsync(string? targetChatId = null, CancellationToken ct = default);
        Task SendKarpooshehRequestNotificationAsync(KarpooshehRequest request, CancellationToken ct = default);
        Task SendWalletRequestNotificationAsync(Transaction transaction, CancellationToken ct = default);
        Task SendCreditPlanRequestNotificationAsync(CreditRequest request, CancellationToken ct = default);
        Task SendUserRegisteredNotificationAsync(User user, CancellationToken ct = default);
    }

    public class BaleNotificationService : IBaleNotificationService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IBaleSettingsService _settingsService;
        private readonly ILogger<BaleNotificationService> _logger;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public BaleNotificationService(
            IHttpClientFactory httpClientFactory,
            IBaleSettingsService settingsService,
            ILogger<BaleNotificationService> logger)
            : this(httpClientFactory, settingsService, logger, null)
        {
        }

        public BaleNotificationService(
            IHttpClientFactory httpClientFactory,
            IBaleSettingsService settingsService,
            ILogger<BaleNotificationService> logger,
            IHttpContextAccessor? httpContextAccessor)
        {
            _httpClientFactory = httpClientFactory;
            _settingsService = settingsService;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        private static string HtmlSafe(string? input)
        {
            if (string.IsNullOrEmpty(input)) return "-";
            return WebUtility.HtmlEncode(input.Trim());
        }

        private static string StripHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            return Regex.Replace(html, "<.*?>", string.Empty);
        }

        private string GetBaseUrl(BaleSettings settings)
        {
            var url = settings?.BaseUrl?.Trim();
            if (!string.IsNullOrEmpty(url))
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }
                return url.TrimEnd('/');
            }

            try
            {
                var req = _httpContextAccessor?.HttpContext?.Request;
                if (req != null && !string.IsNullOrEmpty(req.Host.Value))
                {
                    return $"{req.Scheme}://{req.Host.Value}".TrimEnd('/');
                }
            }
            catch
            {
                // ignored
            }

            return string.Empty;
        }

        private static string BuildAbsoluteUrl(string baseUrl, string relativePath)
        {
            var path = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "https://localhost";
            }
            return $"{baseUrl}{path}";
        }

        public async Task<bool> SendMessageAsync(string text, string? chatId = null, string parseMode = "HTML", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();

                if (string.IsNullOrWhiteSpace(chatId) && !settings.Enabled)
                {
                    _logger.LogDebug("اطلاع‌رسانی بله غیرفعال است. پیام ارسال نشد.");
                    return false;
                }

                var effectiveToken = settings.BotToken?.Trim();
                var effectiveChatId = string.IsNullOrWhiteSpace(chatId) ? settings.ChatId?.Trim() : chatId.Trim();

                if (string.IsNullOrWhiteSpace(effectiveToken) || string.IsNullOrWhiteSpace(effectiveChatId))
                {
                    _logger.LogWarning("توکن یا شناسه چت پیام‌رسان بله تنظیم نشده است.");
                    return false;
                }

                var client = _httpClientFactory.CreateClient("BaleClient");
                var url = $"https://tapi.bale.ai/bot{effectiveToken}/sendMessage";

                var payload = new { chat_id = effectiveChatId, text = text, parse_mode = parseMode };
                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("پیام نوتیفیکیشن با موفقیت به بله (چت: {ChatId}) ارسال شد.", effectiveChatId);
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.BadRequest && !string.IsNullOrEmpty(parseMode))
                {
                    var fallbackPayload = new { chat_id = effectiveChatId, text = StripHtmlTags(text) };
                    using var fallbackContent = new StringContent(JsonSerializer.Serialize(fallbackPayload), Encoding.UTF8, "application/json");
                    var retryResponse = await client.PostAsync(url, fallbackContent, ct);
                    if (retryResponse.IsSuccessStatusCode) return true;
                }

                var finalError = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("خطا در ارسال پیام به بله: وضعیت {Status}، پاسخ: {Response}", response.StatusCode, finalError);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "استثنا در ارسال پیام به پیام‌رسان بله.");
                return false;
            }
        }
        public async Task<bool> SendTestNotificationAsync(string? targetChatId = null, CancellationToken ct = default)
        {
            var nowShamsi = MyDate.GetPersianDate(DateTime.Now)?.ToString() ?? DateTime.Now.ToString("yyyy/MM/dd HH:mm");
            var settings = await _settingsService.GetSettingsAsync();
            var title = string.IsNullOrWhiteSpace(settings.CustomTitle) ? "سامانه صدور فاکتور" : settings.CustomTitle;
            var baseUrl = GetBaseUrl(settings);
            var settingsUrl = BuildAbsoluteUrl(baseUrl, "/Admin/Bale/Index");

            var message = $@"🔔 <b>پیام آزمایشی سامانه</b>
🏷️ {HtmlSafe(title)}

✅ ارتباط وب‌سایت با پیام‌رسان بله با موفقیت برقرار شد!
📅 زمان ارسال: {HtmlSafe(nowShamsi)}
🤖 از این پس اعلان‌های درخواست‌های جدید به این چت ارسال خواهند شد.
🔗 <a href=""{settingsUrl}"">مشاهده تنظیمات بله در پنل مدیریت</a>";

            return await SendMessageAsync(message, targetChatId, "HTML", ct);
        }

        public async Task SendKarpooshehRequestNotificationAsync(KarpooshehRequest request, CancellationToken ct = default)
        {
            if (request == null) return;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                if (!settings.Enabled || !settings.NotifyOnKarpooshehRequest) return;

                var nowShamsi = MyDate.GetPersianDate(request.RequestDate != default ? request.RequestDate : DateTime.Now)?.ToString() ?? "-";
                var personTypeName = request.PersonType.ToString().Replace("_", " ");
                var baseUrl = GetBaseUrl(settings);
                var targetPath = request.Id > 0
                    ? $"/Admin/Requests/Details?id={request.Id}"
                    : "/Admin/Requests/Index";
                var targetUrl = BuildAbsoluteUrl(baseUrl, targetPath);

                var message = $@"📋 <b>درخواست جدید شناسه یکتا (کارپوشه)</b>
🏷️ {HtmlSafe(settings.CustomTitle)}

👤 <b>نام متقاضی:</b> {HtmlSafe(request.Name)}
🏢 <b>نوع شخص:</b> {HtmlSafe(personTypeName)}
🆔 <b>کد / شناسه ملی:</b> <code>{HtmlSafe(request.NationalId)}</code>
📞 <b>شماره تماس:</b> <code>{HtmlSafe(request.Phone)}</code>
💼 <b>عنوان شغل/شرکت:</b> {HtmlSafe(request.JobTitle)}
📧 <b>ایمیل:</b> {HtmlSafe(request.Email)}
🔑 <b>رمز عبور مودیان:</b> {(string.IsNullOrWhiteSpace(request.TaxPassword) ? "ثبت نشده" : "ثبت شده")}
📝 <b>توضیحات:</b> {HtmlSafe(request.Description)}
📅 <b>تاریخ ثبت:</b> {HtmlSafe(nowShamsi)}
🔗 <b>شناسه درخواست:</b> #{request.Id}
🔗 <a href=""{targetUrl}"">مشاهده و بررسی درخواست کارپوشه</a>";

                await SendMessageAsync(message, null, "HTML", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال نوتیفیکیشن درخواست کارپوشه به بله.");
            }
        }

        public async Task SendWalletRequestNotificationAsync(Transaction transaction, CancellationToken ct = default)
        {
            if (transaction == null) return;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                if (!settings.Enabled || !settings.NotifyOnWalletRequest) return;

                var nowShamsi = MyDate.GetPersianDate(transaction.Date != default ? transaction.Date : DateTime.Now)?.ToString() ?? "-";
                var amountFormatted = transaction.Price.HasValue ? transaction.Price.Value.ToString("N0") + " ریال" : "نامشخص";
                var payType = transaction.ByCash ? "واریز نقدی" : "کارت به کارت / فیش";
                var baseUrl = GetBaseUrl(settings);
                var targetPath = transaction.Id > 0
                    ? $"/Admin/Transaction/Confirm?id={transaction.Id}"
                    : "/Admin/Transaction/Index";
                var targetUrl = BuildAbsoluteUrl(baseUrl, targetPath);

                var message = $@"💰 <b>درخواست افزایش موجودی کیف پول</b>
🏷️ {HtmlSafe(settings.CustomTitle)}

👤 <b>کاربر:</b> <code>{HtmlSafe(transaction.CustomerUsername)}</code>
💵 <b>مبلغ:</b> {HtmlSafe(amountFormatted)}
💳 <b>شماره کارت مبدا:</b> <code>{HtmlSafe(transaction.SourceCardNumber)}</code>
🧾 <b>رسید بانکی / پیگیری:</b> <code>{HtmlSafe(transaction.BankTransactionReceipt)}</code>
🔄 <b>روش پرداخت:</b> {HtmlSafe(payType)}
📝 <b>توضیحات:</b> {HtmlSafe(transaction.Description)}
📅 <b>تاریخ ثبت:</b> {HtmlSafe(nowShamsi)}
🔗 <b>شناسه تراکنش:</b> #{transaction.Id}
🔗 <a href=""{targetUrl}"">بررسی و تایید تراکنش کیف پول</a>";

                await SendMessageAsync(message, null, "HTML", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال نوتیفیکیشن درخواست افزایش کیف پول به بله.");
            }
        }

        public async Task SendCreditPlanRequestNotificationAsync(CreditRequest request, CancellationToken ct = default)
        {
            if (request == null) return;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                if (!settings.Enabled || !settings.NotifyOnCreditPlanRequest) return;

                var nowShamsi = MyDate.GetPersianDate(request.Date != default ? request.Date : DateTime.Now)?.ToString() ?? "-";
                var priceFormatted = request.Price.HasValue ? request.Price.Value.ToString("N0") + " ریال" : "-";
                var baseUrl = GetBaseUrl(settings);
                var targetPath = !string.IsNullOrWhiteSpace(request.Username)
                    ? $"/Admin/Users/SetExpire?username={Uri.EscapeDataString(request.Username)}"
                    : "/Admin/Users/Index";
                var targetUrl = BuildAbsoluteUrl(baseUrl, targetPath);

                var message = $@"📦 <b>درخواست خرید بسته / دوره اعتباری</b>
🏷️ {HtmlSafe(settings.CustomTitle)}

👤 <b>کاربر:</b> <code>{HtmlSafe(request.Username)}</code>
⏳ <b>مدت دوره:</b> {request.Months} ماه
💵 <b>هزینه:</b> {HtmlSafe(priceFormatted)}
📅 <b>تاریخ:</b> {HtmlSafe(nowShamsi)}
🔗 <b>شناسه:</b> #{request.Id}
🔗 <a href=""{targetUrl}"">مشاهده و مدیریت اعتبار کاربر</a>";

                await SendMessageAsync(message, null, "HTML", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال نوتیفیکیشن خرید بسته اعتباری به بله.");
            }
        }

        public async Task SendUserRegisteredNotificationAsync(User user, CancellationToken ct = default)
        {
            if (user == null) return;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                if (!settings.Enabled || !settings.NotifyOnUserRegister) return;

                var nowShamsi = MyDate.GetPersianDate(user.RegisterDate != default ? user.RegisterDate : DateTime.Now)?.ToString() ?? "-";
                var baseUrl = GetBaseUrl(settings);
                var targetPath = !string.IsNullOrWhiteSpace(user.Username)
                    ? $"/Admin/Users/AddUser?username={Uri.EscapeDataString(user.Username)}"
                    : "/Admin/Users/Index";
                var targetUrl = BuildAbsoluteUrl(baseUrl, targetPath);

                var message = $@"🎉 <b>ثبت‌نام کاربر جدید در سامانه</b>
🏷️ {HtmlSafe(settings.CustomTitle)}

📱 <b>شماره همراه / نام کاربری:</b> <code>{HtmlSafe(user.Username)}</code>
📧 <b>ایمیل:</b> {HtmlSafe(user.Email)}
📅 <b>تاریخ عضویت:</b> {HtmlSafe(nowShamsi)}
🔗 <a href=""{targetUrl}"">مشاهده اطلاعات کاربر در پنل</a>";

                await SendMessageAsync(message, null, "HTML", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال نوتیفیکیشن ثبت‌ نام کاربر به بله.");
            }
        }

    }
}
