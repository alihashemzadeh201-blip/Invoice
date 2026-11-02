using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using Invoice.Models;
using Invoice.Pages.Admin.Stuffs;
using Invoices.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxCollectData;

namespace Invoice.Code
{
    public class BaleUserInfo
    {
        public string ChatId { get; set; } = string.Empty;
        public string? BaleUsername { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? NationalId { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTime? LastActivityDate { get; set; }
    }

    public interface IBaleUserBotService
    {
        Task ProcessUpdateAsync(JsonElement update, CancellationToken ct = default);
        Task<bool> SendUserMessageAsync(string chatId, string text, object? replyMarkup = null, string parseMode = "HTML", CancellationToken ct = default);
        Task<bool> SendUserDocumentAsync(string chatId, byte[] fileBytes, string fileName, string? caption = null, object? replyMarkup = null, CancellationToken ct = default);
        Task<byte[]?> DownloadFileAsync(string fileId, CancellationToken ct = default);
        Task<bool> NotifyUserTaxIdReadyAsync(string chatId, string name, string memoryId, string username, string? loginPassword = null, CancellationToken ct = default);
        Task<bool> SendRegistrationInvoiceAsync(string chatId, int requestId, int amount, string title, string description, CancellationToken ct = default);
        Task<bool> AnswerPreCheckoutQueryAsync(string preCheckoutQueryId, bool ok, string? errorMessage = null, CancellationToken ct = default);
        Task<int> BroadcastMessageAsync(string text, string? specificChatId = null, CancellationToken ct = default);
        Task<List<string>> GetAllUserChatIdsAsync(CancellationToken ct = default);
        Task<List<BaleUserInfo>> GetBaleUsersListAsync(CancellationToken ct = default);
        Task<int> SendMessageToSelectedUsersAsync(string text, IEnumerable<string> targetChatIds, CancellationToken ct = default);
    }

    public class BaleUserBotService : IBaleUserBotService
    {
        private readonly DataContext _context;
        private readonly IBaleSettingsService _settingsService;
        private readonly IBaleNotificationService _adminNotificationService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILogger<BaleUserBotService> _logger;
        private readonly IKarpooshehRegistrationService? _registrationService;
        private readonly IInvoiceSenderService _invoiceSender;
        private readonly IWebHostEnvironment? _env;

        public BaleUserBotService(
            DataContext context,
            IBaleSettingsService settingsService,
            IBaleNotificationService adminNotificationService,
            IHttpClientFactory httpClientFactory,
            IUserActivityLogger activityLogger,
            ILogger<BaleUserBotService> logger,
            IKarpooshehRegistrationService? registrationService = null,
            IInvoiceSenderService? invoiceSender = null,
            IWebHostEnvironment? env = null)
        {
            _context = context;
            _settingsService = settingsService;
            _adminNotificationService = adminNotificationService;
            _httpClientFactory = httpClientFactory;
            _activityLogger = activityLogger;
            _logger = logger;
            _registrationService = registrationService;
            _invoiceSender = invoiceSender ?? new InvoiceSenderService();
            _env = env;
        }

        private static string HtmlSafe(string? input)
        {
            if (string.IsNullOrEmpty(input)) return "-";
            return WebUtility.HtmlEncode(input.Trim());
        }

        public async Task ProcessUpdateAsync(JsonElement update, CancellationToken ct = default)
        {
            try
            {
                if (update.TryGetProperty("pre_checkout_query", out var preCheckout))
                {
                    await HandlePreCheckoutQueryAsync(preCheckout, ct);
                    return;
                }

                if (update.TryGetProperty("callback_query", out var cb))
                {
                    await HandleCallbackQueryAsync(cb, ct);
                    return;
                }

                if (update.TryGetProperty("message", out var msg))
                {
                    await HandleMessageAsync(msg, ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش آپدیت ارسالی به ربات کاربر بله.");
            }
        }

        private async Task HandlePreCheckoutQueryAsync(JsonElement preCheckout, CancellationToken ct)
        {
            var queryId = preCheckout.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var payload = preCheckout.TryGetProperty("invoice_payload", out var pProp) ? pProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(queryId)) return;

            if (string.IsNullOrWhiteSpace(payload) || (!payload.StartsWith("REG_") && !payload.StartsWith("WALLET_")))
            {
                await AnswerPreCheckoutQueryAsync(queryId, false, "شناسه سفارش پرداخت نامعتبر است.", ct);
                return;
            }

            await AnswerPreCheckoutQueryAsync(queryId, true, null, ct);
        }

        public async Task<bool> AnswerPreCheckoutQueryAsync(string preCheckoutQueryId, bool ok, string? errorMessage = null, CancellationToken ct = default)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var token = settings.UserBotToken?.Trim();
                if (string.IsNullOrWhiteSpace(token)) return false;

                var client = _httpClientFactory.CreateClient("BaleClient");
                var url = $"https://tapi.bale.ai/bot{token}/answerPreCheckoutQuery";

                var body = new Dictionary<string, object>
                {
                    ["pre_checkout_query_id"] = preCheckoutQueryId,
                    ["ok"] = ok
                };
                if (!ok && !string.IsNullOrWhiteSpace(errorMessage))
                {
                    body["error_message"] = errorMessage;
                }

                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال پاسخ PreCheckoutQuery در بله.");
                return false;
            }
        }

        private async Task HandleMessageAsync(JsonElement msg, CancellationToken ct)
        {
            string? chatId = null;
            if (msg.TryGetProperty("chat", out var chatProp) && chatProp.TryGetProperty("id", out var cId))
            {
                chatId = cId.ToString();
            }

            if (string.IsNullOrWhiteSpace(chatId)) return;

            string? fromUsername = null;
            string? fromName = null;
            if (msg.TryGetProperty("from", out var fromProp))
            {
                if (fromProp.TryGetProperty("username", out var unProp)) fromUsername = unProp.GetString();
                var fn = fromProp.TryGetProperty("first_name", out var fnProp) ? fnProp.GetString() : "";
                var ln = fromProp.TryGetProperty("last_name", out var lnProp) ? lnProp.GetString() : "";
                fromName = $"{fn} {ln}".Trim();
            }

            // بررسی پرداخت موفق بومی بله (successful_payment)
            if (msg.TryGetProperty("successful_payment", out var sp))
            {
                await HandleSuccessfulPaymentAsync(chatId, sp, fromUsername, ct);
                return;
            }

            // بررسی دریافت سند / فایل (Document)
            if (msg.TryGetProperty("document", out var docProp))
            {
                await HandleDocumentMessageAsync(chatId, docProp, ct);
                return;
            }

            // بررسی دریافت شماره تماس از طریق Contact
            string? contactPhone = null;
            if (msg.TryGetProperty("contact", out var contactProp) && contactProp.TryGetProperty("phone_number", out var pNumProp))
            {
                contactPhone = pNumProp.GetString();
            }

            var text = msg.TryGetProperty("text", out var tProp) ? tProp.GetString()?.Trim() : null;

            var userState = await GetOrCreateStateAsync(chatId, fromUsername, ct);

            // دستور بازگشت به منوی اصلی در هر مرحله
            if (string.Equals(text, "/start", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "منوی اصلی", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "🔙 بازگشت به منوی اصلی", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "بازگشت به منوی اصلی", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "/menu", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "انصراف", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "❌ انصراف", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "/cancel", StringComparison.OrdinalIgnoreCase))
            {
                userState.CurrentStep = "None";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);
                await ShowMainMenuAsync(chatId, ct);
                return;
            }

            // دکمه بازگشت به مرحله قبل
            if (string.Equals(text, "🔙 بازگشت", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "بازگشت", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "/back", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGoBackAsync(chatId, userState, ct);
                return;
            }

            // بررسی دکمه‌های منوی اصلی
            if (string.Equals(text, "📝 ثبت‌نام و دریافت شناسه یکتا", StringComparison.OrdinalIgnoreCase))
            {
                await StartRegistrationFlowAsync(chatId, userState, ct);
                return;
            }

            if (string.Equals(text, "🔗 ورود / اتصال به حساب کاربری", StringComparison.OrdinalIgnoreCase))
            {
                userState.CurrentStep = "Login_Username";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);

                await SendUserMessageAsync(chatId,
                    "🔐 <b>اتصال به حساب کاربری سامانه</b>\n\n" +
                    "لطفاً نام کاربری خود در سامانه (شماره تلفن همراه یا کد ملی / شناسه ملی) را وارد نمایید:",
                    GetCancelReplyKeyboard(),
                    ct: ct);
                return;
            }

            if (string.Equals(text, "👤 وضعیت پرونده و شناسه یکتا", StringComparison.OrdinalIgnoreCase))
            {
                await ShowProfileStatusAsync(chatId, ct);
                return;
            }

            if (string.Equals(text, "💰 کیف پول و موجودی", StringComparison.OrdinalIgnoreCase))
            {
                await ShowWalletStatusAsync(chatId, ct);
                return;
            }

            if (string.Equals(text, "⏳ تمدید دوره و اشتراک", StringComparison.OrdinalIgnoreCase))
            {
                await ShowSubscriptionPlansAsync(chatId, ct);
                return;
            }

            if (string.Equals(text, "🏢 مدیریت و انتخاب پروفایل فعال", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "مدیریت پروفایل", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "/profiles", StringComparison.OrdinalIgnoreCase))
            {
                await ShowResellerProfilesAsync(chatId, ct);
                return;
            }

            if (string.Equals(text, "⭐ کالاهای منتخب و جستجوی شناسه", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "کالاهای منتخب", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "/favorites", StringComparison.OrdinalIgnoreCase))
            {
                await ShowFavoritesMenuAsync(chatId, ct);
                return;
            }

            if (string.Equals(text, "📄 ارسال فاکتور جدید", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "❌ ابطال فاکتور", StringComparison.OrdinalIgnoreCase))
            {
                await HandleInvoiceMenuRequestAsync(chatId, userState, text, ct);
                return;
            }

            if (string.Equals(text, "❓ راهنما و پشتیبانی", StringComparison.OrdinalIgnoreCase))
            {
                await SendUserMessageAsync(chatId,
                    "💡 <b>راهنمای ربات سامانه مودیان و صدور فاکتور</b>\n\n" +
                    "این ربات به شما امکان می‌دهد:\n" +
                    "• به صورت مستقیم ثبت‌نام کرده و درخواست دریافت کلید خصوصی و شناسه یکتای مالیاتی دهید.\n" +
                    "• هزینه خدمات را از طریق درگاه امن پیام‌رسان بله پرداخت نمایید.\n" +
                    "• کیف پول خود را شارژ کرده و اشتراک خود را تمدید فرمایید.\n" +
                    "• فاکتورهای خود را به سامانه مودیان ارسال و یا در صورت نیاز ابطال نمایید.\n\n" +
                    "در صورت بروز هرگونه مشکل یا سوال با پشتیبانی در ارتباط باشید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            // پردازش گام‌های ورودی کاربر بر اساس استیت جاری
            await ProcessStepInputAsync(chatId, userState, text, contactPhone, fromUsername, ct);
        }

        private async Task HandleInvoiceMenuRequestAsync(string chatId, BaleUserState userState, string actionTitle, CancellationToken ct)
        {
            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            var effectiveProfile = await GetEffectiveProfileAsync(user, userState, ct);
            if (user == null || effectiveProfile == null || string.IsNullOrWhiteSpace(effectiveProfile.MemoryId))
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ <b>حساب کاربری یا شناسه یکتای مالیاتی شما هنوز فعال نشده است.</b>\n\n" +
                    "جهت صدور یا ابطال صورتحساب، ابتدا باید از طریق دکمه «📝 ثبت‌نام و دریافت شناسه یکتا» اقدام نموده و یا در صورت داشتن حساب از دکمه «🔗 ورود / اتصال به حساب کاربری» استفاده نمایید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            if (actionTitle.Contains("ارسال"))
            {
                userState.CurrentStep = "None";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);

                await SendUserMessageAsync(chatId,
                    "📄 <b>ارسال صورتحساب الکترونیکی به سامانه مودیان</b>\n\n" +
                    "شما می‌توانید فاکتورهای خود را به دو روش ارسال کنید:\n" +
                    "1️⃣ <b>ارسال فایل اکسل:</b> فایل اکسل استاندارد فاکتورها را در همین چت ارسال نمایید.\n" +
                    "2️⃣ <b>ثبت سریع تعاملی:</b> از دکمه زیر جهت ثبت مرحله‌به‌مرحله فاکتور تکی استفاده کنید.",
                    new
                    {
                        inline_keyboard = new[]
                        {
                            new[] { new { text = "➕ ثبت فاکتور تکی (مرحله‌به‌مرحله)", callback_data = "inv_create_wizard" } },
                            new[] { new { text = "📥 دریافت نمونه فایل اکسل", callback_data = "inv_download_template" } },
                            new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } }
                        }
                    },
                    ct: ct);
            }
            else
            {
                userState.CurrentStep = "Invoice_Cancel_WaitTaxId";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);

                var invQuery = _context.Invoices
                    .Where(i => i.Sender == user.Username && i.Status == InvoiceStatus.موفق && i.Ins == InvoiceTopic.اصلی);
                if (effectiveProfile != null)
                {
                    invQuery = invQuery.Where(i => i.ProfileId == effectiveProfile.Id);
                }

                var recentInvoices = await invQuery
                    .OrderByDescending(i => i.Id)
                    .Take(5)
                    .ToListAsync(ct);

                if (!recentInvoices.Any())
                {
                    await SendUserMessageAsync(chatId,
                        "ℹ️ در حال حاضر هیچ فاکتور ارسالی موفقی در سوابق اخیر شما جهت ابطال یافت نشد.\n\nدر صورتی که شماره مالیاتی ۲۲ رقمی فاکتور را دارید، لطفاً آن را در چت ارسال فرمایید:",
                        GetCancelReplyKeyboard(),
                        ct: ct);
                    return;
                }

                var buttons = new List<object[]>();
                foreach (var inv in recentInvoices)
                {
                    var shortTaxId = inv.Taxid != null && inv.Taxid.Length > 10 ? inv.Taxid.Substring(0, 10) + "..." : (inv.Taxid ?? "-");
                    var label = $"ابطال فاکتور #{inv.Id} ({shortTaxId})";
                    buttons.Add(new[] { new { text = label, callback_data = $"inv_cancel:{inv.Id}" } });
                }
                buttons.Add(new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } });

                await SendUserMessageAsync(chatId,
                    "❌ <b>ابطال فاکتور مالیاتی</b>\n\nلطفاً فاکتور مورد نظر جهت ابطال را از لیست زیر انتخاب کنید یا شماره مالیاتی ۲۲ رقمی آن را در چت ارسال نمایید:",
                    new { inline_keyboard = buttons.ToArray() },
                    ct: ct);
            }
        }

        private async Task ShowWalletStatusAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user == null)
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ شما هنوز حسابی در سامانه متصل نکرده‌اید. لطفاً ابتدا ثبت‌نام کرده یا وارد حساب خود شوید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            var balanceRials = user.Vallet.ToString("N0");
            var balanceTomans = (user.Vallet / 10).ToString("N0");
            var perInvoice = (await Utils.GetPerInvoiceCostAsync(user, _context)).ToString("N0");

            var text = $"💰 <b>کیف پول و اعتبارات</b>\n\n" +
                       $"• موجودی کیف پول: <b>{balanceRials} ریال</b> ({balanceTomans} تومان)\n" +
                       $"• هزینه هر فاکتور: <b>{perInvoice} ریال</b>\n\n" +
                       $"جهت شارژ سریع کیف پول از طریق پرداخت داخلی بله، یکی از مبالغ زیر را انتخاب نمایید:";

            var markup = new
            {
                inline_keyboard = new[]
                {
                    new[]
                    {
                        new { text = "💳 ۱,۰۰۰,۰۰۰ ریال", callback_data = "wallet_charge:1000000" },
                        new { text = "💳 ۲,۵۰۰,۰۰۰ ریال", callback_data = "wallet_charge:2500000" }
                    },
                    new[]
                    {
                        new { text = "💳 ۵,۰۰۰,۰۰۰ ریال", callback_data = "wallet_charge:5000000" },
                        new { text = "💳 ۱۰,۰۰۰,۰۰۰ ریال", callback_data = "wallet_charge:10000000" }
                    },
                    new[]
                    {
                        new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" }
                    }
                }
            };

            await SendUserMessageAsync(chatId, text, markup, ct: ct);
        }

        private async Task ShowSubscriptionPlansAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user == null)
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ شما هنوز حسابی در سامانه متصل نکرده‌اید. لطفاً ابتدا ثبت‌نام کرده یا وارد حساب خود شوید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            var expireStr = user.ExpireDate.HasValue
                ? (MyDate.GetPersianDate(user.ExpireDate.Value)?.ToString() ?? "-")
                : "نامحدود / تعریف نشده";

            var periods = await _context.CreditPeriods.ToListAsync(ct);
            var text = $"⏳ <b>وضعیت اشتراک و تمدید دوره</b>\n\n" +
                       $"• تاریخ پایان اشتراک فعلی: <b>{expireStr}</b>\n\n" +
                       $"لیست بسته‌های اشتراک قابل تمدید:";

            var buttons = new List<object[]>();
            foreach (var p in periods)
            {
                var priceStr = p.Price.ToString("N0");
                buttons.Add(new[]
                {
                    new { text = $"📦 {p.Title} ({priceStr} ریال)", callback_data = $"sub_renew:{p.Id}" }
                });
            }
            buttons.Add(new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } });

            await SendUserMessageAsync(chatId, text, new { inline_keyboard = buttons.ToArray() }, ct: ct);
        }

        private async Task ShowProfileStatusAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user != null)
            {
                var isReseller = string.Equals(user.Role, "reseller", StringComparison.OrdinalIgnoreCase);
                var effectiveProfile = await GetEffectiveProfileAsync(user, chatId, ct);
                var p = effectiveProfile ?? user.Profile;
                var memoryId = !string.IsNullOrWhiteSpace(p?.MemoryId) ? $"<code>{p.MemoryId}</code>" : "در انتظار صدور";
                var tin = p?.TinCode ?? "-";
                var fullName = p != null ? $"{p.FirstName} {p.Lastname}".Trim() : user.Username;

                var text = $"👤 <b>مشخصات حساب کاربری و پرونده مالیاتی</b>\n\n" +
                           $"• متقاضی/مودی: <b>{HtmlSafe(fullName)}</b>\n" +
                           $"• نام کاربری: <code>{user.Username}</code>\n" +
                           (isReseller && p != null ? $"• 🏢 <b>پروفایل فعال جاری:</b> {HtmlSafe(p.DisplayName)}\n" : "") +
                           $"• کد اقتصادی / شناسه ملی: <code>{tin}</code>\n" +
                           $"• شناسه یکتای حافظه مالیاتی: {memoryId}\n" +
                           $"• موجودی کیف پول: <b>{user.Vallet:N0} ریال</b>\n\n" +
                           $"✅ وضعیت: <b>حساب متصل و آماده صدور صورتحساب</b>";

                await SendUserMessageAsync(chatId, text, GetBackToMenuInlineKeyboard(), ct: ct);
                return;
            }

            // اگر کاربر سیستم نبود، بررسی درخواست کارپوشه ثبت‌شده
            var request = await _context.KarpooshehRequests
                .Where(r => r.BaleChatId == chatId)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);

            if (request != null)
            {
                var statusStr = request.FinishDate.HasValue
                    ? "✅ پایان یافته و پرونده تکمیل گردید."
                    : request.ReceiveDate.HasValue
                        ? "🔍 در حال بررسی و صدور شناسه توسط کارشناسان"
                        : request.IsPaid
                            ? "💳 پرداخت شده و در نوبت کارشناسی"
                            : "⚠️ در انتظار پرداخت وجه ثبت‌نام";

                var reqDate = MyDate.GetPersianDate(request.RequestDate)?.ToString() ?? "-";
                var text = $"📋 <b>وضعیت درخواست ثبت‌نام و اخذ شناسه یکتا</b>\n\n" +
                           $"• شماره پرونده: <b>#{request.Id}</b>\n" +
                           $"• متقاضی: <b>{HtmlSafe(request.Name)}</b>\n" +
                           $"• کد ملی: <code>{request.NationalId}</code>\n" +
                           $"• تاریخ ثبت: {reqDate}\n" +
                           $"• وضعیت فعلی: <b>{statusStr}</b>";

                var buttons = new List<object[]>();
                if (!request.IsPaid)
                {
                    buttons.Add(new[] { new { text = "💳 پرداخت هزینه و فعال‌سازی درخواست", callback_data = $"pay_reg:{request.Id}" } });
                }
                buttons.Add(new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } });

                await SendUserMessageAsync(chatId, text, new { inline_keyboard = buttons.ToArray() }, ct: ct);
                return;
            }

            await SendUserMessageAsync(chatId,
                "ℹ️ در حال حاضر هیچ درخواست یا حسابی متصل به این حساب بله ثبت نشده است.\n\nجهت شروع می‌توانید از دکمه «📝 ثبت‌نام و دریافت شناسه یکتا» استفاده نمایید.",
                new
                {
                    inline_keyboard = new[]
                    {
                        new[] { new { text = "📝 ثبت‌نام و دریافت شناسه یکتا", callback_data = "reg_start" } },
                        new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } }
                    }
                },
                ct: ct);
        }

        private async Task StartRegistrationFlowAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            // بررسی درخواست قبلی فعال
            var existingReq = await _context.KarpooshehRequests
                .Where(r => r.BaleChatId == chatId && r.FinishDate == null && r.CancelDate == null)
                .FirstOrDefaultAsync(ct);

            if (existingReq != null)
            {
                if (!existingReq.IsPaid)
                {
                    await SendUserMessageAsync(chatId,
                        $"⚠️ شما یک درخواست ثبت‌نام فعال در سامانه دارید (پرونده #{existingReq.Id}) که هزینه آن هنوز پرداخت نشده است.\n\nجهت تکمیل مراحل، لطفاً پرداخت را انجام دهید:",
                        new
                        {
                            inline_keyboard = new[]
                            {
                                new[] { new { text = "💳 پرداخت هزینه ثبت‌نام", callback_data = $"pay_reg:{existingReq.Id}" } },
                                new[] { new { text = "❌ انصراف و ثبت درخواست جدید", callback_data = $"cancel_reg:{existingReq.Id}" } },
                                new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } }
                            }
                        },
                        ct: ct);
                    return;
                }

                await SendUserMessageAsync(chatId,
                    $"✅ درخواست شما قبلاً با کد پیگیری #{existingReq.Id} پرداخت و ثبت شده و در نوبت تیم کارشناسی قرار دارد. به محض صدور شناسه یکتا، به شما اطلاع‌رسانی خواهد شد.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            userState.CurrentStep = "Reg_PersonType";
            userState.StateDataJson = JsonSerializer.Serialize(new Dictionary<string, string>());
            await _context.SaveChangesAsync(ct);

            var text = "📝 <b>مرحله ۱ از ۷: نوع شخص متقاضی</b>\n\n" +
                       "لطفاً نوع شخصیت خود را جهت صدور شناسه یکتای مالیاتی انتخاب نمایید:";

            var markup = new
            {
                inline_keyboard = new[]
                {
                    new[] { new { text = "👤 شخص حقیقی", callback_data = "reg_pt:Real" } },
                    new[] { new { text = "🏢 شخص حقوقی (شرکت / موسسه)", callback_data = "reg_pt:Legal" } },
                    new[] { new { text = "❌ انصراف و بازگشت به منوی اصلی", callback_data = "reg_cancel" } }
                }
            };

            await SendUserMessageAsync(chatId, text, markup, ct: ct);
        }

        private async Task HandleGoBackAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            var step = userState.CurrentStep ?? "None";
            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            switch (step)
            {
                case "Login_Password":
                    userState.CurrentStep = "Login_Username";
                    await _context.SaveChangesAsync(ct);
                    await SendUserMessageAsync(chatId,
                        "🔐 <b>اتصال به حساب کاربری سامانه</b>\n\n" +
                        "لطفاً نام کاربری خود در سامانه (شماره تلفن همراه یا کد ملی / شناسه ملی) را وارد نمایید:",
                        GetCancelReplyKeyboard(),
                        ct: ct);
                    return;

                case "Login_Username":
                case "Reg_PersonType":
                case "Favorite_Search":
                case "InvWiz_BuyerType":
                case "Invoice_Cancel_WaitTaxId":
                    userState.CurrentStep = "None";
                    userState.StateDataJson = null;
                    await _context.SaveChangesAsync(ct);
                    await ShowMainMenuAsync(chatId, ct);
                    return;

                case "InvWiz_BuyerId":
                    await StartInvoiceWizardAsync(chatId, userState, ct);
                    return;

                case "InvWiz_ItemSelect":
                    if (data.TryGetValue("BuyerType", out var btSel) && btSel == "consumer")
                    {
                        await StartInvoiceWizardAsync(chatId, userState, ct);
                    }
                    else
                    {
                        userState.CurrentStep = "InvWiz_BuyerId";
                        await _context.SaveChangesAsync(ct);
                        var isLegalId = data.TryGetValue("BuyerType", out var btId) && btId == "legal";
                        var promptBuyer = isLegalId
                            ? "🏢 <b>مرحله ۲ از ۵: شناسه ملی شرکت / شخص حقوقی</b>\n\nلطفاً <b>شناسه ملی ۱۱ رقمی</b> خریدار حقوقی را بدون فاصله یا خط تیره وارد نمایید:"
                            : "📝 <b>مرحله ۲ از ۵: کد ملی خریدار</b>\n\nلطفاً <b>کد ملی ۱۰ رقمی</b> خریدار حقیقی را بدون فاصله یا خط تیره وارد نمایید:";
                        await SendUserMessageAsync(chatId, promptBuyer, GetCancelReplyKeyboard(), ct: ct);
                    }
                    return;

                case "InvWiz_ItemSearch":
                    userState.CurrentStep = "InvWiz_ItemSelect";
                    await _context.SaveChangesAsync(ct);
                    await ShowItemSelectMenuAsync(chatId, userState, ct);
                    return;

                case "InvWiz_Quantity":
                    userState.CurrentStep = "InvWiz_ItemSelect";
                    await _context.SaveChangesAsync(ct);
                    await ShowItemSelectMenuAsync(chatId, userState, ct);
                    return;

                case "InvWiz_Fee":
                    userState.CurrentStep = "InvWiz_Quantity";
                    await _context.SaveChangesAsync(ct);
                    var itemTitleQ = data.TryGetValue("ItemTitle", out var itQ) ? itQ : "-";
                    var itemCodeQ = data.TryGetValue("ItemCode", out var icQ) ? icQ : "-";
                    var vatQ = data.TryGetValue("ItemVat", out var vQ) ? vQ : "10";
                    await SendUserMessageAsync(chatId,
                        $"📦 <b>کالای انتخابی:</b> {HtmlSafe(itemTitleQ)}\n" +
                        $"شناسه: <code>{itemCodeQ}</code> | نرخ ارزش افزوده: <b>{vatQ}%</b>\n\n" +
                        "لطفاً <b>تعداد یا مقدار</b> را به صورت عدد وارد فرمایید (مثال: <code>1</code> یا <code>2.5</code>):",
                        GetCancelReplyKeyboard(),
                        ct: ct);
                    return;

                case "InvWiz_CheckoutType":
                    userState.CurrentStep = "InvWiz_Fee";
                    await _context.SaveChangesAsync(ct);
                    await ShowFeePromptAsync(chatId, userState, ct);
                    return;

                case "InvWiz_Confirm":
                    if (data.TryGetValue("BuyerType", out var btConf) && btConf == "consumer")
                    {
                        userState.CurrentStep = "InvWiz_Fee";
                        await _context.SaveChangesAsync(ct);
                        await ShowFeePromptAsync(chatId, userState, ct);
                    }
                    else
                    {
                        userState.CurrentStep = "InvWiz_CheckoutType";
                        await _context.SaveChangesAsync(ct);
                        await ShowCheckoutTypeMenuAsync(chatId, userState, ct);
                    }
                    return;

                case "Reg_Name":
                    userState.CurrentStep = "Reg_PersonType";
                    await _context.SaveChangesAsync(ct);
                    var textPt = "📝 <b>مرحله ۱ از ۷: نوع شخص متقاضی</b>\n\n" +
                                 "لطفاً نوع شخصیت خود را جهت صدور شناسه یکتای مالیاتی انتخاب نمایید:";
                    var markupPt = new
                    {
                        inline_keyboard = new[]
                        {
                            new[] { new { text = "👤 شخص حقیقی", callback_data = "reg_pt:Real" } },
                            new[] { new { text = "🏢 شخص حقوقی (شرکت / موسسه)", callback_data = "reg_pt:Legal" } },
                            new[] { new { text = "❌ انصراف و بازگشت به منوی اصلی", callback_data = "reg_cancel" } }
                        }
                    };
                    await SendUserMessageAsync(chatId, textPt, markupPt, ct: ct);
                    return;

                case "Reg_LatinName":
                    userState.CurrentStep = "Reg_Name";
                    await _context.SaveChangesAsync(ct);
                    var isLegal = data.TryGetValue("PersonType", out var pt) && pt == "Legal";
                    var promptName = isLegal
                        ? "📝 <b>مرحله ۲ از ۷: نام کامل شرکت یا موسسه</b>\n\nلطفاً نام رسمی ثبت‌شده شرکت را وارد نمایید:"
                        : "📝 <b>مرحله ۲ از ۷: نام و نام خانوادگی</b>\n\nلطفاً نام و نام خانوادگی خود را مطابق کارت ملی وارد نمایید:";
                    await SendUserMessageAsync(chatId, promptName, GetCancelReplyKeyboard(), ct: ct);
                    return;

                case "Reg_NationalId":
                    userState.CurrentStep = "Reg_LatinName";
                    await _context.SaveChangesAsync(ct);
                    await SendUserMessageAsync(chatId,
                        "📝 <b>مرحله ۳ از ۷: نام عمومی (انگلیسی)</b>\n\n" +
                        "لطفاً نام خود یا شرکت را به انگلیسی، با حروف الفبا و بدون فاصله وارد نمایید (جهت درج در گواهی امضای دیجیتال CSR):\n" +
                        "<i>مثال: Alireza یا Fanavaran</i>",
                        GetCancelReplyKeyboard(),
                        ct: ct);
                    return;

                case "Reg_Phone":
                    userState.CurrentStep = "Reg_NationalId";
                    await _context.SaveChangesAsync(ct);
                    var isLegalNat = data.TryGetValue("PersonType", out var ptNat) && ptNat == "Legal";
                    var idTitle = isLegalNat ? "شناسه ملی شرکت (۱۱ رقم)" : "شماره ملی (۱۰ رقم)";
                    await SendUserMessageAsync(chatId, $"📝 <b>مرحله ۴ از ۷: {idTitle}</b>\n\nلطفاً {idTitle} را بدون خط تیره یا فاصله وارد فرمایید:", GetCancelReplyKeyboard(), ct: ct);
                    return;

                case "Reg_JobTitle":
                    userState.CurrentStep = "Reg_Phone";
                    await _context.SaveChangesAsync(ct);
                    await SendUserMessageAsync(chatId,
                        "📝 <b>مرحله ۵ از ۷: شماره همراه</b>\n\n" +
                        "لطفاً شماره تلفن همراه فعال خود را وارد نمایید (مانند <code>09121234567</code>) یا از طریق دکمه زیر شماره خود را به اشتراک بگذارید:",
                        GetPhoneReplyKeyboard(),
                        ct: ct);
                    return;

                case "Reg_UsernameChoice":
                    userState.CurrentStep = "Reg_JobTitle";
                    await _context.SaveChangesAsync(ct);
                    await SendUserMessageAsync(chatId,
                        "📝 <b>مرحله ۶ از ۷: عنوان شغل یا فعالیت</b>\n\n" +
                        "لطفاً صنف، شغل یا عنوان فعالیت اقتصادی خود را وارد فرمایید:\n<i>مثال: فروشگاه لوازم یدکی، پیمانکاری ساختمان، خدمات فناوری اطلاعات</i>",
                        GetCancelReplyKeyboard(),
                        ct: ct);
                    return;

                case "Reg_Confirm":
                    userState.CurrentStep = "Reg_UsernameChoice";
                    await _context.SaveChangesAsync(ct);
                    await PromptUsernameChoiceAsync(chatId, data, ct);
                    return;

                default:
                    userState.CurrentStep = "None";
                    userState.StateDataJson = null;
                    await _context.SaveChangesAsync(ct);
                    await ShowMainMenuAsync(chatId, ct);
                    return;
            }
        }

        private async Task PromptUsernameChoiceAsync(string chatId, Dictionary<string, string> data, CancellationToken ct)
        {
            var natId = data.TryGetValue("NationalId", out var nid) ? nid : "";
            var phone = data.TryGetValue("Phone", out var ph) ? ph : "";

            var prompt = "📝 <b>مرحله ۷ از ۷: انتخاب نام کاربری سامانه</b>\n\n" +
                         "لطفاً مشخص فرمایید که تمایل دارید نام کاربری شما جهت ورود به سامانه کدام مورد باشد:\n\n" +
                         $"1️⃣ شماره تلفن همراه: <code>{phone}</code>\n" +
                         $"2️⃣ کد ملی / شناسه ملی: <code>{natId}</code>\n\n" +
                         "جهت انتخاب، یکی از گزینه‌های زیر را لمس نمایید یا عدد ۱ یا ۲ را ارسال فرمایید:";

            var markup = new
            {
                inline_keyboard = new[]
                {
                    new[] { new { text = $"📱 شماره همراه ({phone})", callback_data = "reg_un:phone" } },
                    new[] { new { text = $"🆔 کد/شناسه ملی ({natId})", callback_data = "reg_un:natid" } }
                }
            };

            await SendUserMessageAsync(chatId, prompt, markup, ct: ct);
        }

        private async Task ProcessStepInputAsync(string chatId, BaleUserState userState, string? text, string? contactPhone, string? fromUsername, CancellationToken ct)
        {
            var step = userState.CurrentStep ?? "None";
            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            switch (step)
            {
                case "Login_Username":
                    var cleanLoginUser = (text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(cleanLoginUser) || (!Regex.IsMatch(cleanLoginUser, @"^09\d{9}$") && !Regex.IsMatch(cleanLoginUser, @"^\d{10,11}$")))
                    {
                        await SendUserMessageAsync(chatId, "❌ لطفاً یک نام کاربری معتبر (شماره همراه ۱۱ رقمی مانند <code>09121234567</code> یا کد ملی / شناسه ملی ۱۰-۱۱ رقمی) وارد نمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["LoginUser"] = cleanLoginUser;
                    userState.CurrentStep = "Login_Password";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);

                    await SendUserMessageAsync(chatId, "🔑 لطفاً رمز عبور حساب کاربری خود در سامانه را وارد نمایید:", GetCancelReplyKeyboard(), ct: ct);
                    return;

                case "Login_Password":
                    var targetUser = data.TryGetValue("LoginUser", out var uName) ? uName : null;
                    if (string.IsNullOrWhiteSpace(targetUser) || string.IsNullOrWhiteSpace(text))
                    {
                        userState.CurrentStep = "None";
                        await _context.SaveChangesAsync(ct);
                        await ShowMainMenuAsync(chatId, ct);
                        return;
                    }

                    var dbUser = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Username == targetUser, ct);
                    if (dbUser == null || PasswordHashManager.Verify(dbUser.Password, text.Trim()) == PasswordCheckResult.Failed)
                    {
                        await SendUserMessageAsync(chatId, "❌ نام کاربری یا رمز عبور اشتباه است. لطفاً مجدداً تلاش نمایید:", GetCancelReplyKeyboard(), ct: ct);
                        userState.CurrentStep = "None";
                        await _context.SaveChangesAsync(ct);
                        await ShowMainMenuAsync(chatId, ct);
                        return;
                    }

                    dbUser.BaleChatId = chatId;
                    dbUser.BaleUsername = fromUsername;
                    userState.CurrentStep = "None";
                    userState.StateDataJson = null;
                    await _context.SaveChangesAsync(ct);

                    await SendUserMessageAsync(chatId,
                        $"🎉 <b>اتصال حساب با موفقیت انجام شد!</b>\n\nبه سامانه خوش آمدید، <b>{HtmlSafe(dbUser.DisplayName)}</b> عزیز.\nاکنون می‌توانید کلیه امور مالیاتی و فاکتورهای خود را از طریق دکمه‌های منو مدیریت نمایید.",
                        ct: ct);
                    await ShowMainMenuAsync(chatId, ct);
                    return;

                case "Reg_Name":
                    if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 3)
                    {
                        await SendUserMessageAsync(chatId, "❌ لطفاً نام و نام خانوادگی خود (یا نام کامل شرکت) را به درستی وارد نمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["Name"] = text.Trim();
                    userState.CurrentStep = "Reg_LatinName";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);

                    await SendUserMessageAsync(chatId,
                        "📝 <b>مرحله ۳ از ۷: نام عمومی (انگلیسی)</b>\n\n" +
                        "لطفاً نام خود یا شرکت را به انگلیسی، با حروف الفبا و بدون فاصله وارد نمایید (جهت درج در گواهی امضای دیجیتال CSR):\n" +
                        "<i>مثال: Alireza یا Fanavaran</i>",
                        GetCancelReplyKeyboard(),
                        ct: ct);
                    return;

                case "Reg_LatinName":
                    var cleanLatin = (text ?? "").Trim();
                    if (!Regex.IsMatch(cleanLatin, @"^[a-zA-Z]+$"))
                    {
                        await SendUserMessageAsync(chatId, "❌ نام عمومی باید صرفاً شامل حروف انگلیسی و بدون فاصله یا اعداد باشد. لطفاً مجدداً وارد فرمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["LatinName"] = cleanLatin;
                    userState.CurrentStep = "Reg_NationalId";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);

                    var isLegal = data.TryGetValue("PersonType", out var pt) && pt == "Legal";
                    var idTitle = isLegal ? "شناسه ملی شرکت (۱۱ رقم)" : "شماره ملی (۱۰ رقم)";
                    await SendUserMessageAsync(chatId, $"📝 <b>مرحله ۴ از ۷: {idTitle}</b>\n\nلطفاً {idTitle} را بدون خط تیره یا فاصله وارد فرمایید:", GetCancelReplyKeyboard(), ct: ct);
                    return;

                case "Reg_NationalId":
                    var natId = (text ?? "").Trim();
                    if (!Regex.IsMatch(natId, @"^\d{10,11}$"))
                    {
                        await SendUserMessageAsync(chatId, "❌ کد ملی / شناسه ملی باید عددی بین ۱۰ تا ۱۱ رقم باشد. لطفاً مجدداً وارد نمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    var preReq = await _context.KarpooshehRequests.FirstOrDefaultAsync(r => r.NationalId == natId && r.FinishDate == null, ct);
                    if (preReq != null)
                    {
                        await SendUserMessageAsync(chatId, "⚠️ قبلاً با این کدملی/شناسه ملی درخواستی در سامانه ثبت شده است. لطفاً وضعیت پرونده خود را پیگیری نمایید.", ct: ct);
                        userState.CurrentStep = "None";
                        await _context.SaveChangesAsync(ct);
                        await ShowMainMenuAsync(chatId, ct);
                        return;
                    }
                    data["NationalId"] = natId;
                    userState.CurrentStep = "Reg_Phone";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);

                    await SendUserMessageAsync(chatId,
                        "📝 <b>مرحله ۵ از ۷: شماره همراه</b>\n\n" +
                        "لطفاً شماره تلفن همراه فعال خود را وارد نمایید (مانند <code>09121234567</code>) یا از طریق دکمه زیر شماره خود را به اشتراک بگذارید:",
                        GetPhoneReplyKeyboard(),
                        ct: ct);
                    return;

                case "Reg_Phone":
                    var phoneVal = !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone : text;
                    phoneVal = (phoneVal ?? "").Trim();
                    // نرمال‌سازی فرمت‌های مختلف شماره تلفن
                    if (phoneVal.StartsWith("+98"))
                        phoneVal = "0" + phoneVal.Substring(3);
                    else if (phoneVal.StartsWith("98") && phoneVal.Length == 12)
                        phoneVal = "0" + phoneVal.Substring(2);
                    if (!Regex.IsMatch(phoneVal, @"^09\d{9}$"))
                    {
                        await SendUserMessageAsync(chatId, "❌ شماره تماس نامعتبر است. لطفاً شماره موبایل ۱۱ رقمی را با پیش‌شماره 09 وارد فرمایید:", GetPhoneReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["Phone"] = phoneVal;
                    userState.CurrentStep = "Reg_JobTitle";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);

                    await SendUserMessageAsync(chatId,
                        "📝 <b>مرحله ۶ از ۷: عنوان شغل یا فعالیت</b>\n\n" +
                        "لطفاً صنف، شغل یا عنوان فعالیت اقتصادی خود را وارد فرمایید:\n<i>مثال: فروشگاه لوازم یدکی، پیمانکاری ساختمان، خدمات فناوری اطلاعات</i>",
                        GetCancelReplyKeyboard(),
                        ct: ct);
                    return;

                case "Reg_JobTitle":
                    if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 2)
                    {
                        await SendUserMessageAsync(chatId, "❌ لطفاً عنوان شغل خود را وارد نمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["JobTitle"] = text.Trim();
                    userState.CurrentStep = "Reg_UsernameChoice";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);

                    await PromptUsernameChoiceAsync(chatId, data, ct);
                    return;

                case "Reg_UsernameChoice":
                    var tUn = (text ?? "").Trim();
                    var phOpt = data.TryGetValue("Phone", out var pVal) ? pVal : "";
                    var idOpt = data.TryGetValue("NationalId", out var iVal) ? iVal : "";
                    if (tUn == "1" || tUn.Contains("همراه") || tUn.Contains("تلفن") || tUn.Contains("شماره") || tUn.Contains("موبایل"))
                    {
                        data["PreferredUsername"] = phOpt;
                        data["UsernameType"] = "Phone";
                    }
                    else if (tUn == "2" || tUn.Contains("کد") || tUn.Contains("شناسه") || tUn.Contains("ملی"))
                    {
                        data["PreferredUsername"] = idOpt;
                        data["UsernameType"] = "NationalId";
                    }
                    else
                    {
                        await SendUserMessageAsync(chatId, "❌ لطفاً یکی از دو گزینه بالا را لمس نمایید یا عدد ۱ (شماره همراه) یا ۲ (کد ملی) را ارسال فرمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    userState.CurrentStep = "Reg_Confirm";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);

                    await ShowRegistrationSummaryAsync(chatId, data, ct);
                    return;

                case "Reg_PostalCode":
                    // در صورت وجود استیت‌های قدیمی در حال اجرا، مستقیماً به انتخاب نام کاربری هدایت می‌شود
                    userState.CurrentStep = "Reg_UsernameChoice";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);
                    await PromptUsernameChoiceAsync(chatId, data, ct);
                    return;

                case "Favorite_Search":
                    var searchQuery = (text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(searchQuery) || searchQuery.Length < 2)
                    {
                        await SendUserMessageAsync(chatId,
                            "⚠️ عبارت جستجو باید حداقل ۲ کاراکتر باشد. لطفاً مجدداً ارسال فرمایید یا دکمه بازگشت را انتخاب نمایید:",
                            GetCancelReplyKeyboard(),
                            ct: ct);
                        return;
                    }

                    var searchUser = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
                    if (searchUser == null)
                    {
                        userState.CurrentStep = "None";
                        await _context.SaveChangesAsync(ct);
                        await ShowMainMenuAsync(chatId, ct);
                        return;
                    }

                    var foundStuffs = await _context.Stuffs
                        .Where(s => s.Code.Contains(searchQuery) || s.Title.Contains(searchQuery))
                        .Take(6)
                        .ToListAsync(ct);

                    if (!foundStuffs.Any())
                    {
                        await SendUserMessageAsync(chatId,
                            $"🔍 متاسفانه موردی برای عبارت «<b>{HtmlSafe(searchQuery)}</b>» در شناسه‌های مالیاتی کالا و خدمات یافت نشد.\n\nمی‌توانید کلمه دیگری را جستجو فرمایید یا از دکمه بازگشت استفاده نمایید:",
                            GetCancelReplyKeyboard(),
                            ct: ct);
                        return;
                    }

                    var existingFavCodes = (await _context.UserFavoriteStuffs
                        .Where(f => f.Username == searchUser.Username)
                        .Select(f => f.StuffCode)
                        .ToListAsync(ct))
                        .ToHashSet();

                    var resultSb = new StringBuilder();
                    resultSb.AppendLine($"🔍 <b>نتایج جستجو برای:</b> «{HtmlSafe(searchQuery)}»\n");

                    var resultButtons = new List<object[]>();
                    for (int i = 0; i < foundStuffs.Count; i++)
                    {
                        var st = foundStuffs[i];
                        var isFav = existingFavCodes.Contains(st.Code);
                        resultSb.AppendLine($"{i + 1}️⃣ <b>{HtmlSafe(st.Title)}</b>");
                        resultSb.AppendLine($"   شناسه: <code>{st.Code}</code> | نرخ ارزش افزوده: <code>{st.VatRate}%</code>\n");

                        if (isFav)
                        {
                            resultButtons.Add(new[]
                            {
                                new { text = $"❌ حذف {st.Code} از علاقه‌مندی", callback_data = $"fav_del:{st.Code}" }
                            });
                        }
                        else
                        {
                            resultButtons.Add(new[]
                            {
                                new { text = $"⭐ افزودن {st.Code} به علاقه‌مندی", callback_data = $"fav_add:{st.Code}" }
                            });
                        }
                    }

                    userState.CurrentStep = "None";
                    await _context.SaveChangesAsync(ct);

                    resultButtons.Add(new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } });

                    await SendUserMessageAsync(chatId, resultSb.ToString(), new { inline_keyboard = resultButtons.ToArray() }, ct: ct);
                    return;

                case "InvWiz_BuyerId":
                    var cleanBuyerId = Utils.ConvertPersianNumberToEnglish((text ?? "").Trim());
                    var bType = data.TryGetValue("BuyerType", out var btVal) ? btVal : "real";
                    var expectedLen = bType == "legal" ? 11 : 10;
                    var titleStr = bType == "legal" ? "شناسه ملی ۱۱ رقمی شرکت" : "کد ملی ۱۰ رقمی";
                    if (!Regex.IsMatch(cleanBuyerId, $@"^\d{{{expectedLen}}}$"))
                    {
                        await SendUserMessageAsync(chatId, $"❌ {titleStr} نامعتبر است. لطفاً دقیقاً {expectedLen} رقم بدون فاصله یا خط تیره وارد فرمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["BuyerId"] = cleanBuyerId;
                    userState.CurrentStep = "InvWiz_ItemSelect";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);
                    await ShowItemSelectMenuAsync(chatId, userState, ct);
                    return;

                case "InvWiz_ItemSearch":
                    await ProcessItemSearchInputAsync(chatId, userState, text, ct);
                    return;

                case "InvWiz_Quantity":
                    var cleanAm = Utils.ConvertPersianNumberToEnglish((text ?? "").Trim());
                    if (!decimal.TryParse(cleanAm, NumberStyles.Any, CultureInfo.InvariantCulture, out var am) || am <= 0)
                    {
                        await SendUserMessageAsync(chatId, "❌ تعداد یا مقدار باید یک عدد بزرگتر از صفر باشد. لطفاً مجدداً وارد فرمایید (مثال: <code>1</code> یا <code>2.5</code>):", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["Am"] = am.ToString(CultureInfo.InvariantCulture);
                    userState.CurrentStep = "InvWiz_Fee";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);
                    await ShowFeePromptAsync(chatId, userState, ct);
                    return;

                case "InvWiz_Fee":
                    var cleanFee = Utils.ConvertPersianNumberToEnglish((text ?? "").Trim());
                    if (!decimal.TryParse(cleanFee, NumberStyles.Any, CultureInfo.InvariantCulture, out var fee) || fee < 0)
                    {
                        await SendUserMessageAsync(chatId, "❌ مبلغ واحد (فی) باید عددی نامنفی به ریال باشد. لطفاً مجدداً وارد فرمایید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    data["Fee"] = fee.ToString(CultureInfo.InvariantCulture);
                    if (data.TryGetValue("BuyerType", out var btForFee) && btForFee == "consumer")
                    {
                        data["CheckoutType"] = "1"; // نقدی
                        userState.CurrentStep = "InvWiz_Confirm";
                        userState.StateDataJson = JsonSerializer.Serialize(data);
                        await _context.SaveChangesAsync(ct);
                        await ShowInvoiceWizardConfirmCardAsync(chatId, userState, ct);
                        return;
                    }
                    userState.CurrentStep = "InvWiz_CheckoutType";
                    userState.StateDataJson = JsonSerializer.Serialize(data);
                    await _context.SaveChangesAsync(ct);
                    await ShowCheckoutTypeMenuAsync(chatId, userState, ct);
                    return;

                case "Invoice_Cancel_WaitTaxId":
                    var cleanTaxId = (text ?? "").Trim().ToUpperInvariant();
                    if (cleanTaxId.Length != 22 || !Regex.IsMatch(cleanTaxId, @"^[A-Z0-9]{22}$"))
                    {
                        await SendUserMessageAsync(chatId, "❌ شماره منحصر به فرد مالیاتی باید ۲۲ رقم/حرف انگلیسی باشد. لطفاً مجدداً وارد فرمایید یا انصراف دهید:", GetCancelReplyKeyboard(), ct: ct);
                        return;
                    }
                    userState.CurrentStep = "None";
                    userState.StateDataJson = null;
                    await _context.SaveChangesAsync(ct);
                    await ShowCancelTaxIdConfirmAsync(chatId, cleanTaxId, ct);
                    return;

                default:
                    await ShowMainMenuAsync(chatId, ct);
                    return;
            }
        }

        private async Task ShowRegistrationSummaryAsync(string chatId, Dictionary<string, string> data, CancellationToken ct)
        {
            var settings = await _settingsService.GetSettingsAsync();
            var feeRials = settings.RegistrationFee > 0 ? settings.RegistrationFee : 5000000;
            var feeTomans = (feeRials / 10).ToString("N0");

            var ptTitle = data.TryGetValue("PersonType", out var pt) && pt == "Legal" ? "حقوقی (شرکت)" : "حقیقی";
            var name = data.TryGetValue("Name", out var n) ? n : "-";
            var latin = data.TryGetValue("LatinName", out var l) ? l : "-";
            var natId = data.TryGetValue("NationalId", out var id) ? id : "-";
            var phone = data.TryGetValue("Phone", out var p) ? p : "-";
            var job = data.TryGetValue("JobTitle", out var j) ? j : "-";
            var prefUsername = data.TryGetValue("PreferredUsername", out var pu) && !string.IsNullOrWhiteSpace(pu)
                ? pu
                : phone;

            var summaryText = $"🔍 <b>پیش‌نمایش و تایید اطلاعات ثبت‌نام</b>\n\n" +
                              $"• نوع شخص: <b>{ptTitle}</b>\n" +
                              $"• نام و نام خانوادگی: <b>{HtmlSafe(name)}</b>\n" +
                              $"• نام عمومی لاتین: <code>{latin}</code>\n" +
                              $"• کدملی / شناسه ملی: <code>{natId}</code>\n" +
                              $"• شماره همراه: <code>{phone}</code>\n" +
                              $"• عنوان شغل: <b>{HtmlSafe(job)}</b>\n" +
                              $"• نام کاربری انتخابی سامانه: <code>{prefUsername}</code>\n\n" +
                              $"💰 <b>هزینه صدور شناسه یکتا و ایجاد کارپوشه:</b> {feeRials:N0} ریال ({feeTomans} تومان)\n\n" +
                              $"در صورت تایید، لطفاً دکمه «تایید و پرداخت وجه» را لمس نمایید تا فاکتور پرداخت پیام‌رسان بله برای شما صادر گردد:";

            var markup = new
            {
                inline_keyboard = new[]
                {
                    new[] { new { text = "💳 تایید و پرداخت وجه", callback_data = "reg_pay_now" } },
                    new[]
                    {
                        new { text = "🔙 ویرایش و بازگشت", callback_data = "reg_back" },
                        new { text = "❌ انصراف و شروع مجدد", callback_data = "reg_cancel" }
                    }
                }
            };

            await SendUserMessageAsync(chatId, summaryText, markup, ct: ct);
        }

        private async Task HandleCallbackQueryAsync(JsonElement cb, CancellationToken ct)
        {
            var queryId = cb.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var data = cb.TryGetProperty("data", out var dProp) ? dProp.GetString() : null;

            string? chatId = null;
            if (cb.TryGetProperty("message", out var msgProp) &&
                msgProp.TryGetProperty("chat", out var chatProp) &&
                chatProp.TryGetProperty("id", out var cId))
            {
                chatId = cId.ToString();
            }

            if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(data)) return;

            string? fromUsername = null;
            if (cb.TryGetProperty("from", out var fromProp) && fromProp.TryGetProperty("username", out var unProp))
            {
                fromUsername = unProp.GetString();
            }

            var userState = await GetOrCreateStateAsync(chatId, fromUsername, ct);

            // انتخاب نوع شخص
            if (data.StartsWith("reg_pt:"))
            {
                var pt = data.Substring("reg_pt:".Length);
                var stateDict = string.IsNullOrWhiteSpace(userState.StateDataJson)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

                stateDict["PersonType"] = pt;
                userState.CurrentStep = "Reg_Name";
                userState.StateDataJson = JsonSerializer.Serialize(stateDict);
                await _context.SaveChangesAsync(ct);

                var prompt = pt == "Legal"
                    ? "📝 <b>مرحله ۲ از ۷: نام کامل شرکت یا موسسه</b>\n\nلطفاً نام رسمی ثبت‌شده شرکت را وارد نمایید:"
                    : "📝 <b>مرحله ۲ از ۷: نام و نام خانوادگی</b>\n\nلطفاً نام و نام خانوادگی خود را مطابق کارت ملی وارد نمایید:";

                await SendUserMessageAsync(chatId, prompt, GetCancelReplyKeyboard(), ct: ct);
                return;
            }

            // انتخاب نام کاربری
            if (data.StartsWith("reg_un:"))
            {
                var choice = data.Substring("reg_un:".Length);
                var stateDict = string.IsNullOrWhiteSpace(userState.StateDataJson)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

                var ph = stateDict.TryGetValue("Phone", out var pVal) ? pVal : "";
                var nid = stateDict.TryGetValue("NationalId", out var nVal) ? nVal : "";

                if (choice == "phone")
                {
                    stateDict["PreferredUsername"] = ph;
                    stateDict["UsernameType"] = "Phone";
                }
                else
                {
                    stateDict["PreferredUsername"] = nid;
                    stateDict["UsernameType"] = "NationalId";
                }

                userState.CurrentStep = "Reg_Confirm";
                userState.StateDataJson = JsonSerializer.Serialize(stateDict);
                await _context.SaveChangesAsync(ct);

                await ShowRegistrationSummaryAsync(chatId, stateDict, ct);
                return;
            }

            // بازگشت به مرحله قبل از پیش‌نمایش
            if (data == "reg_back")
            {
                await HandleGoBackAsync(chatId, userState, ct);
                return;
            }

            // انصراف از ثبت‌نام
            if (data == "reg_cancel")
            {
                userState.CurrentStep = "None";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);
                await SendUserMessageAsync(chatId, "❌ فرآیند ثبت‌نام لغو شد.", ct: ct);
                await ShowMainMenuAsync(chatId, ct);
                return;
            }

            // پرداخت نهایی ثبت‌نام
            if (data == "reg_pay_now")
            {
                await CreateAndSendRegistrationInvoiceAsync(chatId, userState, fromUsername, ct);
                return;
            }

            // پرداخت مجدد برای درخواست موجود
            if (data.StartsWith("pay_reg:"))
            {
                var reqIdStr = data.Substring("pay_reg:".Length);
                if (int.TryParse(reqIdStr, out var reqId))
                {
                    var req = await _context.KarpooshehRequests.FindAsync(reqId);
                    if (req != null && !req.IsPaid)
                    {
                        var settings = await _settingsService.GetSettingsAsync();
                        var fee = settings.RegistrationFee > 0 ? settings.RegistrationFee : 5000000;
                        await SendRegistrationInvoiceAsync(chatId, req.Id, fee,
                            "هزینه ثبت‌نام و اخذ شناسه یکتا",
                            $"صدور شناسه یکتای مالیاتی برای {req.Name} - کدملی {req.NationalId}",
                            ct);
                        return;
                    }
                }
            }

            // انصراف از درخواست ثبت‌نام قبلی و شروع مجدد
            if (data.StartsWith("cancel_reg:"))
            {
                var reqIdStr = data.Substring("cancel_reg:".Length);
                if (int.TryParse(reqIdStr, out var cancelReqId))
                {
                    var reqToCancel = await _context.KarpooshehRequests.FindAsync(cancelReqId);
                    if (reqToCancel != null && !reqToCancel.IsPaid)
                    {
                        reqToCancel.CancelDate = DateTime.Now;
                        reqToCancel.FinishDate = DateTime.Now;
                        await _context.SaveChangesAsync(ct);
                    }
                }
                userState.CurrentStep = "None";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);
                await SendUserMessageAsync(chatId, "✅ درخواست قبلی لغو شد. در حال شروع ثبت‌نام جدید...", ct: ct);
                await StartRegistrationFlowAsync(chatId, userState, ct);
                return;
            }

            // شارژ کیف پول
            if (data.StartsWith("wallet_charge:"))
            {
                var amountStr = data.Substring("wallet_charge:".Length);
                if (int.TryParse(amountStr, out var amount) && amount > 0)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
                    if (user != null)
                    {
                        var tx = new Transaction
                        {
                            CustomerUsername = user.Username,
                            Price = amount,
                            Date = DateTime.Now,
                            ByCash = false,
                            Description = "شارژ کیف پول از طریق پیام‌رسان بله",
                            SourceCardNumber = "BalePayment"
                        };
                        _context.Transactions.Add(tx);
                        await _context.SaveChangesAsync(ct);

                        await SendBaleInvoiceAsync(chatId,
                            "شارژ کیف پول سامانه مودیان",
                            $"افزایش موجودی حساب کاربری {user.Username} به مبلغ {amount:N0} ریال",
                            $"WALLET_{tx.Id}_{amount}",
                            amount,
                            ct);
                        return;
                    }
                }
            }

            // عملیات خنثی برای دکمه‌های غیرفعال
            if (data == "noop")
            {
                return;
            }

            // بازگشت به منوی اصلی
            if (data == "main_menu")
            {
                userState.CurrentStep = "None";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);
                await ShowMainMenuAsync(chatId, ct);
                return;
            }

            // نمایش منوی علاقه‌مندی‌ها
            if (data == "fav_menu")
            {
                await ShowFavoritesMenuAsync(chatId, ct);
                return;
            }

            // شروع فرآیند ثبت‌نام
            if (data == "reg_start")
            {
                await StartRegistrationFlowAsync(chatId, userState, ct);
                return;
            }

            // انتخاب پروفایل فعال توسط نماینده (Reseller)
            if (data.StartsWith("reseller_prof:"))
            {
                var profIdStr = data.Substring("reseller_prof:".Length);
                if (int.TryParse(profIdStr, out var targetProfId))
                {
                    var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
                    if (user != null && string.Equals(user.Role, "reseller", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetProf = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == targetProfId && (p.Creator == user.Username || p.Id == user.ProfileId), ct);
                        if (targetProf != null)
                        {
                            userState.ActiveProfileId = targetProf.Id;
                            userState.LastUpdated = DateTime.Now;
                            await _context.SaveChangesAsync(ct);

                            await SendUserMessageAsync(chatId,
                                $"✅ <b>پروفایل فعال با موفقیت تغییر یافت.</b>\n\n" +
                                $"🏢 <b>پروفایل انتخابی:</b> {HtmlSafe(targetProf.DisplayName)}\n" +
                                $"🔑 <b>شناسه یکتای حافظه مالیاتی:</b> <code>{targetProf.MemoryId ?? "-"}</code>\n" +
                                $"🆔 <b>کد اقتصادی / ملی:</b> <code>{targetProf.TinCode ?? "-"}</code>\n\n" +
                                $"از این پس کلیه فاکتورهای جدید ارسالی با این مشخصات صادر و امضا خواهند شد.",
                                replyMarkup: GetMainUserReplyKeyboard(true, isReseller: true),
                                ct: ct);
                            return;
                        }
                    }
                }
            }

            // شروع فرآیند جستجوی شناسه کالا/خدمت
            if (data == "fav_search_start")
            {
                userState.CurrentStep = "Favorite_Search";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);

                await SendUserMessageAsync(chatId,
                    "🔍 <b>جستجوی شناسه کالا و خدمات مالیاتی</b>\n\n" +
                    "لطفاً بخشی از عنوان، شرح یا کد ۱۳ رقمی شناسه کالا/خدمت مورد نظر خود را ارسال فرمایید:\n" +
                    "(جهت انصراف، دکمه «انصراف» یا «بازگشت» را لمس فرمایید)",
                    GetCancelReplyKeyboard(),
                    ct: ct);
                return;
            }

            // افزودن کالا به لیست علاقه‌مندی‌ها
            if (data.StartsWith("fav_add:"))
            {
                var code = data.Substring("fav_add:".Length).Trim();
                var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
                if (user != null)
                {
                    var exists = await _context.UserFavoriteStuffs.AnyAsync(f => f.Username == user.Username && f.StuffCode == code, ct);
                    if (!exists)
                    {
                        _context.UserFavoriteStuffs.Add(new UserFavoriteStuff
                        {
                            Username = user.Username,
                            StuffCode = code,
                            AddedDate = DateTime.Now
                        });
                        await _context.SaveChangesAsync(ct);
                    }

                    var stuff = await _context.Stuffs.FindAsync(new object[] { code }, ct);
                    var title = stuff?.Title ?? code;
                    var favAddMarkup = new
                    {
                        inline_keyboard = new[]
                        {
                            new[] { new { text = "⭐ مشاهده لیست منتخب‌ها", callback_data = "fav_menu" } },
                            new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } }
                        }
                    };
                    await SendUserMessageAsync(chatId,
                        $"⭐ کالا با شناسه <code>{code}</code> («<b>{HtmlSafe(title)}</b>») با موفقیت به لیست کالاهای منتخب شما اضافه شد.",
                        replyMarkup: favAddMarkup,
                        ct: ct);
                    return;
                }
            }

            // حذف کالا از لیست علاقه‌مندی‌ها
            if (data.StartsWith("fav_del:"))
            {
                var code = data.Substring("fav_del:".Length).Trim();
                var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
                if (user != null)
                {
                    var fav = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.Username == user.Username && f.StuffCode == code, ct);
                    if (fav != null)
                    {
                        _context.UserFavoriteStuffs.Remove(fav);
                        await _context.SaveChangesAsync(ct);
                    }

                    var favDelMarkup = new
                    {
                        inline_keyboard = new[]
                        {
                            new[] { new { text = "⭐ مشاهده لیست منتخب‌ها", callback_data = "fav_menu" } },
                            new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } }
                        }
                    };
                    await SendUserMessageAsync(chatId,
                        $"❌ شناسه کالا <code>{code}</code> از لیست علاقه‌مندی‌های شما حذف گردید.",
                        replyMarkup: favDelMarkup,
                        ct: ct);
                    return;
                }
            }

            // دریافت نمونه فایل اکسل صورتحساب
            if (data == "inv_download_template")
            {
                await HandleDownloadTemplateAsync(chatId, ct);
                return;
            }

            // شروع ویزارد صدور فاکتور تکی
            if (data == "inv_create_wizard")
            {
                await StartInvoiceWizardAsync(chatId, userState, ct);
                return;
            }

            // انتخاب نوع خریدار در ویزارد
            if (data.StartsWith("inv_wiz_bt:"))
            {
                var bt = data.Substring("inv_wiz_bt:".Length);
                await HandleBuyerTypeSelectedAsync(chatId, userState, bt, ct);
                return;
            }

            // انتخاب کالا با شناسه در ویزارد
            if (data.StartsWith("inv_wiz_item:"))
            {
                var code = data.Substring("inv_wiz_item:".Length);
                await SelectItemByCodeAsync(chatId, userState, code, ct);
                return;
            }

            // کلیک روی دکمه جستجوی کالا
            if (data == "inv_wiz_item_search_btn")
            {
                userState.CurrentStep = "InvWiz_ItemSearch";
                await _context.SaveChangesAsync(ct);
                await SendUserMessageAsync(chatId,
                    "🔍 <b>جستجو و انتخاب شناسه کالا/خدمت</b>\n\n" +
                    "لطفاً شناسه ۱۳ رقمی کالا را وارد فرمایید یا بخشی از نام کالا را جهت جستجو ارسال نمایید:",
                    GetCancelReplyKeyboard(),
                    ct: ct);
                return;
            }

            // تایید فی پیش‌فرض در ویزارد
            if (data.StartsWith("inv_wiz_fee_default:"))
            {
                var feeVal = data.Substring("inv_wiz_fee_default:".Length);
                await HandleDefaultFeeSelectedAsync(chatId, userState, feeVal, ct);
                return;
            }

            // انتخاب روش تسویه در ویزارد
            if (data.StartsWith("inv_wiz_setm:"))
            {
                var setmVal = data.Substring("inv_wiz_setm:".Length);
                await HandleCheckoutTypeSelectedAsync(chatId, userState, setmVal, ct);
                return;
            }

            // تایید نهایی و ارسال فاکتور ویزارد به سامانه مودیان
            if (data == "inv_wiz_send")
            {
                await SendSingleInvoiceWizardAsync(chatId, userState, ct);
                return;
            }

            // بازگشت در ویزارد
            if (data == "inv_wiz_back")
            {
                await HandleGoBackAsync(chatId, userState, ct);
                return;
            }

            // انصراف از ویزارد
            if (data == "inv_wiz_cancel")
            {
                userState.CurrentStep = "None";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);
                await SendUserMessageAsync(chatId, "❌ صدور فاکتور لغو شد.", ct: ct);
                await ShowMainMenuAsync(chatId, ct);
                return;
            }

            // تایید ارسال گروهی فاکتورهای اکسل
            if (data == "inv_excel_confirm")
            {
                await SendExcelDraftInvoicesAsync(chatId, ct);
                return;
            }

            // لغو و حذف پیش‌نویس‌های اکسل
            if (data == "inv_excel_cancel")
            {
                await CancelExcelDraftsAsync(chatId, ct);
                return;
            }

            // نمایش کارت تایید ابطال فاکتور از لیست اخیر
            if (data.StartsWith("inv_cancel:"))
            {
                var invIdStr = data.Substring("inv_cancel:".Length);
                if (int.TryParse(invIdStr, out var invId))
                {
                    await ShowCancelInvoiceConfirmAsync(chatId, invId, ct);
                }
                return;
            }

            // تایید نهایی ابطال فاکتور انتخاب‌شده با ID
            if (data.StartsWith("inv_cancel_confirm:"))
            {
                var invIdStr = data.Substring("inv_cancel_confirm:".Length);
                if (int.TryParse(invIdStr, out var invId))
                {
                    await ExecuteInvoiceCancellationAsync(chatId, invId, null, ct);
                }
                return;
            }

            // تایید نهایی ابطال با شناسه مالیاتی ۲۲ رقمی دستی
            if (data.StartsWith("inv_cancel_taxid_confirm:"))
            {
                var taxIdVal = data.Substring("inv_cancel_taxid_confirm:".Length);
                await ExecuteInvoiceCancellationAsync(chatId, null, taxIdVal, ct);
                return;
            }
        }

        private async Task CreateAndSendRegistrationInvoiceAsync(string chatId, BaleUserState userState, string? fromUsername, CancellationToken ct)
        {
            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson);

            if (data == null || !data.TryGetValue("NationalId", out var natId) || !data.TryGetValue("Name", out var name))
            {
                await SendUserMessageAsync(chatId, "⚠️ اطلاعات ثبت‌نام ناقص است. لطفاً فرآیند را از ابتدا آغاز فرمایید.", ct: ct);
                userState.CurrentStep = "None";
                await _context.SaveChangesAsync(ct);
                await ShowMainMenuAsync(chatId, ct);
                return;
            }

            var settings = await _settingsService.GetSettingsAsync();
            var fee = settings.RegistrationFee > 0 ? settings.RegistrationFee : 5000000;

            var prefUn = data.TryGetValue("PreferredUsername", out var pu) && !string.IsNullOrWhiteSpace(pu)
                ? pu
                : (data.TryGetValue("Phone", out var pDef) ? pDef : natId);

            var req = new KarpooshehRequest
            {
                Name = name,
                LatinName = data.TryGetValue("LatinName", out var l) ? l : null,
                NationalId = natId,
                Phone = data.TryGetValue("Phone", out var p) ? p : "",
                JobTitle = data.TryGetValue("JobTitle", out var j) ? j : "",
                PostalCode = null,
                Description = $"نام کاربری انتخابی: {prefUn}",
                PersonType = data.TryGetValue("PersonType", out var pt) && pt == "Legal" ? PersonType.حقوقی : PersonType.حقیقی,
                RequestDate = DateTime.Now,
                BaleChatId = chatId,
                BaleUsername = fromUsername,
                IsPaid = fee <= 0,
                PaymentAmount = fee
            };

            _context.KarpooshehRequests.Add(req);
            await _context.SaveChangesAsync(ct);

            if (_registrationService != null)
            {
                try
                {
                    await _registrationService.ProvisionUserAndProfileForRequestAsync(req, "123456", ct);
                }
                catch (Exception exReg)
                {
                    _logger.LogError(exReg, "خطا در تولید خودکار کلیدهای RSA و ثبت‌نام متقاضی برای درخواست #{RequestId}", req.Id);
                }
            }

            if (fee <= 0)
            {
                userState.CurrentStep = "None";
                userState.StateDataJson = null;
                await _context.SaveChangesAsync(ct);

                await SendUserMessageAsync(chatId,
                    $"✅ درخواست اخذ شناسه یکتای مالیاتی شما با کد پیگیری <b>#{req.Id}</b> با موفقیت ثبت شد و جهت بررسی در اختیار کارشناسان قرار گرفت.\nبه محض اقدام، در همین ربات به شما اطلاع‌رسانی خواهد شد.",
                    ct: ct);
                await ShowMainMenuAsync(chatId, ct);
                await _adminNotificationService.SendKarpooshehRequestNotificationAsync(req, ct);
                return;
            }

            userState.CurrentStep = "Registration_AwaitingPayment";
            await _context.SaveChangesAsync(ct);

            var sent = await SendRegistrationInvoiceAsync(chatId, req.Id, fee,
                "هزینه ثبت‌نام و صدور شناسه یکتا",
                $"صدور شناسه یکتای حافظه مالیاتی برای {req.Name} - کدملی {req.NationalId}",
                ct);

            // ارسال اعلان ثبت درخواست به ربات ادمین
            await _adminNotificationService.SendKarpooshehRequestNotificationAsync(req, ct);

            if (!sent)
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ در حال حاضر درگاه پرداخت آنلاین پیام‌رسان بله فعال نمی‌باشد یا توکن پذیرنده تنظیم نشده است.\nدرخواست شما با کد پیگیری #" + req.Id + " ثبت گردید و پس از بررسی با شما تماس گرفته خواهد شد.",
                    ct: ct);
            }
        }

        private async Task HandleSuccessfulPaymentAsync(string chatId, JsonElement sp, string? fromUsername, CancellationToken ct)
        {
            var payload = sp.TryGetProperty("invoice_payload", out var pProp) ? pProp.GetString() : null;
            var totalAmount = sp.TryGetProperty("total_amount", out var aProp) ? aProp.GetInt32() : 0;
            var providerChargeId = sp.TryGetProperty("provider_payment_charge_id", out var pcProp) ? pcProp.GetString() : null;
            var telegramChargeId = sp.TryGetProperty("telegram_payment_charge_id", out var cProp) ? cProp.GetString() : null;
            var chargeId = !string.IsNullOrWhiteSpace(providerChargeId) ? providerChargeId : telegramChargeId;

            if (string.IsNullOrWhiteSpace(payload)) return;

            if (payload.StartsWith("REG_"))
            {
                var reqIdStr = payload.Substring("REG_".Length);
                if (int.TryParse(reqIdStr, out var reqId))
                {
                    var req = await _context.KarpooshehRequests.FindAsync(reqId);
                    if (req != null)
                    {
                        req.IsPaid = true;
                        req.PaymentReference = chargeId ?? $"BALE_{DateTime.Now.Ticks}";
                        req.PaymentAmount = totalAmount;
                        req.BaleChatId = chatId;
                        if (!string.IsNullOrWhiteSpace(fromUsername)) req.BaleUsername = fromUsername;
                        await _context.SaveChangesAsync(ct);

                        if (_registrationService != null)
                        {
                            try
                            {
                                await _registrationService.ProvisionUserAndProfileForRequestAsync(req, "123456", ct);
                            }
                            catch (Exception exReg)
                            {
                                _logger.LogError(exReg, "خطا در بررسی/ثبت کاربر برای درخواست پرداخت‌شده #{RequestId}", req.Id);
                            }
                        }

                        // بازنشانی وضعیت مکالمه کاربر
                        var state = await _context.BaleUserStates.FindAsync(chatId);
                        if (state != null)
                        {
                            state.CurrentStep = "None";
                            state.StateDataJson = null;
                            await _context.SaveChangesAsync(ct);
                        }

                        // ارسال پیام تایید به کاربر
                        var userMsg = "🎉 <b>پرداخت شما با موفقیت در بله انجام شد!</b>\n\n" +
                                      $"• شماره پرونده: <b>#{req.Id}</b>\n" +
                                      $"• کد رهگیری پرداخت: <code>{req.PaymentReference}</code>\n" +
                                      $"• مبلغ پرداختی: <b>{totalAmount:N0} ریال</b>\n\n" +
                                      "درخواست شما برای کارشناسان تیم ادمین ارسال گردید. به محض اخذ شناسه یکتا و فعال‌سازی حساب، در همین ربات به شما اطلاع‌رسانی خواهد شد.";
                        await SendUserMessageAsync(chatId, userMsg, ct: ct);
                        await ShowMainMenuAsync(chatId, ct);

                        // ارسال نوتیفیکیشن با اولویت بالا به ربات ادمین
                        await _adminNotificationService.SendKarpooshehRequestNotificationAsync(req, ct);
                        await _activityLogger.LogAsync("بله", "پرداخت ثبت نام بله", $"پرداخت موفق هزینه ثبت‌نام بله توسط متقاضی {req.Name} (مبلغ: {totalAmount})", entityId: req.Id.ToString(), ct: ct);
                    }
                }
            }
            else if (payload.StartsWith("WALLET_"))
            {
                var parts = payload.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var txId))
                {
                    var tx = await _context.Transactions.Include(t => t.Customer).FirstOrDefaultAsync(t => t.Id == txId, ct);
                    if (tx != null)
                    {
                        tx.Confirm = true;
                        tx.ConfirmDate = DateTime.Now;
                        tx.BankTransactionReceipt = chargeId ?? $"BALE_{DateTime.Now.Ticks}";
                        if (tx.Customer != null)
                        {
                            tx.Customer.Vallet += totalAmount;
                            tx.Customer.LastChargeDate = DateTime.Now;
                        }
                        await _context.SaveChangesAsync(ct);

                        var msgSuccess = "🎉 <b>کیف پول شما با موفقیت شارژ گردید!</b>\n\n" +
                                         $"• مبلغ شارژ: <b>{totalAmount:N0} ریال</b>\n" +
                                         $"• کد پیگیری بانک: <code>{tx.BankTransactionReceipt}</code>\n" +
                                         $"• موجودی فعلی: <b>{tx.Customer?.Vallet:N0} ریال</b>";
                        await SendUserMessageAsync(chatId, msgSuccess, ct: ct);
                        await _adminNotificationService.SendWalletRequestNotificationAsync(tx, ct);
                    }
                }
            }
        }

        public async Task<bool> SendRegistrationInvoiceAsync(string chatId, int requestId, int amount, string title, string description, CancellationToken ct = default)
        {
            return await SendBaleInvoiceAsync(chatId, title, description, $"REG_{requestId}", amount, ct);
        }

        private async Task<bool> SendBaleInvoiceAsync(string chatId, string title, string description, string payload, int amount, CancellationToken ct = default)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var token = settings.UserBotToken?.Trim();
                var providerToken = settings.UserBotPaymentProviderToken?.Trim();

                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogWarning("توکن ربات کاربر بله جهت صدور فاکتور پرداخت تنظیم نشده است.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(providerToken))
                {
                    _logger.LogWarning("توکن پذیرنده پرداخت بله (UserBotPaymentProviderToken) تنظیم نشده است.");
                    return false;
                }

                if (title.Length > 32) title = title.Substring(0, 32);
                if (description.Length > 255) description = description.Substring(0, 255);
                if (payload.Length > 128) payload = payload.Substring(0, 128);

                var client = _httpClientFactory.CreateClient("BaleClient");
                var url = $"https://tapi.bale.ai/bot{token}/sendInvoice";

                var body = new Dictionary<string, object>
                {
                    ["chat_id"] = chatId,
                    ["title"] = title,
                    ["description"] = description,
                    ["payload"] = payload,
                    ["provider_token"] = providerToken,
                    ["currency"] = "IRR",
                    ["prices"] = new[]
                    {
                        new { label = title, amount = amount }
                    }
                };

                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content, ct);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("خطا در صدور فاکتور پرداخت بله: {Error}", err);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "استثنا در صدور فاکتور پرداخت بله برای چت {ChatId}", chatId);
                return false;
            }
        }

        public async Task<bool> NotifyUserTaxIdReadyAsync(string chatId, string name, string memoryId, string username, string? loginPassword = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;

            var passText = !string.IsNullOrWhiteSpace(loginPassword)
                ? $"\n• رمز عبور موقت: <code>{loginPassword}</code>"
                : "";

            var text = $"🎉 <b>تبریک! شناسه یکتای مالیاتی شما صادر شد</b>\n\n" +
                       $"متقاضی گرامی <b>{HtmlSafe(name)}</b>،\n" +
                       $"فرآیند ثبت‌نام و دریافت کلیدهای مالیاتی شما با موفقیت انجام شد:\n\n" +
                       $"• شناسه یکتای حافظه مالیاتی: <code>{memoryId}</code>\n" +
                       $"• نام کاربری سامانه: <code>{username}</code>{passText}\n\n" +
                       $"از هم‌اکنون می‌توانید با استفاده از منوی زیر، فاکتورهای خود را به سامانه مودیان ارسال فرمایید.";

            return await SendUserMessageAsync(chatId, text, GetMainUserReplyKeyboard(true), ct: ct);
        }

        public async Task<List<string>> GetAllUserChatIdsAsync(CancellationToken ct = default)
        {
            var userChats = await _context.Users
                .Where(u => !string.IsNullOrWhiteSpace(u.BaleChatId))
                .Select(u => u.BaleChatId!)
                .ToListAsync(ct);

            var reqChats = await _context.KarpooshehRequests
                .Where(r => !string.IsNullOrWhiteSpace(r.BaleChatId))
                .Select(r => r.BaleChatId!)
                .ToListAsync(ct);

            var stateChats = await _context.BaleUserStates
                .Where(s => !string.IsNullOrWhiteSpace(s.ChatId))
                .Select(s => s.ChatId!)
                .ToListAsync(ct);

            return userChats
                .Concat(reqChats)
                .Concat(stateChats)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct()
                .ToList();
        }

        public async Task<int> BroadcastMessageAsync(string text, string? specificChatId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var targets = !string.IsNullOrWhiteSpace(specificChatId)
                ? new List<string> { specificChatId.Trim() }
                : await GetAllUserChatIdsAsync(ct);

            var successCount = 0;
            foreach (var cid in targets)
            {
                try
                {
                    var sent = await SendUserMessageAsync(cid, text, ct: ct);
                    if (sent) successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "خطا در ارسال پیام همگانی به چت {ChatId}", cid);
                }
            }

            return successCount;
        }

        public async Task<int> SendMessageToSelectedUsersAsync(string text, IEnumerable<string> targetChatIds, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text) || targetChatIds == null) return 0;

            var distinctTargets = targetChatIds
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct()
                .ToList();

            var successCount = 0;
            foreach (var cid in distinctTargets)
            {
                try
                {
                    var sent = await SendUserMessageAsync(cid, text, ct: ct);
                    if (sent) successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "خطا در ارسال پیام به کاربر بله با شناسه چت {ChatId}", cid);
                }
            }

            return successCount;
        }

        public async Task<List<BaleUserInfo>> GetBaleUsersListAsync(CancellationToken ct = default)
        {
            var dict = new Dictionary<string, BaleUserInfo>();

            // 1. کاربران ثبت‌شده در سامانه با BaleChatId
            var users = await _context.Users
                .Include(u => u.Profile)
                .Where(u => !string.IsNullOrWhiteSpace(u.BaleChatId))
                .ToListAsync(ct);

            foreach (var u in users)
            {
                var cid = u.BaleChatId!.Trim();
                if (dict.ContainsKey(cid)) continue;

                dict[cid] = new BaleUserInfo
                {
                    ChatId = cid,
                    BaleUsername = u.BaleUsername,
                    DisplayName = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : u.Username,
                    PhoneNumber = u.Username,
                    NationalId = u.Profile?.TinCode,
                    Source = "کاربر سامانه",
                    LastActivityDate = u.RegisterDate
                };
            }

            // 2. متقاضیان کارپوشه با BaleChatId
            var requests = await _context.KarpooshehRequests
                .Where(r => !string.IsNullOrWhiteSpace(r.BaleChatId))
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync(ct);

            foreach (var r in requests)
            {
                var cid = r.BaleChatId!.Trim();
                if (dict.TryGetValue(cid, out var existing))
                {
                    if (string.IsNullOrWhiteSpace(existing.NationalId)) existing.NationalId = r.NationalId;
                    if (string.IsNullOrWhiteSpace(existing.PhoneNumber)) existing.PhoneNumber = r.Phone;
                    if (existing.DisplayName == "-" || string.IsNullOrWhiteSpace(existing.DisplayName)) existing.DisplayName = r.Name;
                    if (string.IsNullOrWhiteSpace(existing.BaleUsername)) existing.BaleUsername = r.BaleUsername;
                }
                else
                {
                    dict[cid] = new BaleUserInfo
                    {
                        ChatId = cid,
                        BaleUsername = r.BaleUsername,
                        DisplayName = r.Name,
                        PhoneNumber = r.Phone,
                        NationalId = r.NationalId,
                        Source = "متقاضی کارپوشه",
                        LastActivityDate = r.RequestDate
                    };
                }
            }

            // 3. کاربرانی که با ربات تعامل داشته‌اند (BaleUserState)
            var states = await _context.BaleUserStates
                .Where(s => !string.IsNullOrWhiteSpace(s.ChatId))
                .OrderByDescending(s => s.LastUpdated)
                .ToListAsync(ct);

            foreach (var s in states)
            {
                var cid = s.ChatId.Trim();
                if (dict.TryGetValue(cid, out var existing))
                {
                    if (string.IsNullOrWhiteSpace(existing.BaleUsername)) existing.BaleUsername = s.Username;
                    if (!existing.LastActivityDate.HasValue || existing.LastActivityDate < s.LastUpdated)
                    {
                        existing.LastActivityDate = s.LastUpdated;
                    }
                }
                else
                {
                    string name = "-";
                    string? phone = null;
                    string? natId = null;

                    if (!string.IsNullOrWhiteSpace(s.StateDataJson))
                    {
                        try
                        {
                            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(s.StateDataJson);
                            if (data != null)
                            {
                                if (data.TryGetValue("Name", out var n)) name = n;
                                if (data.TryGetValue("Phone", out var p)) phone = p;
                                if (data.TryGetValue("NationalId", out var nid)) natId = nid;
                            }
                        }
                        catch { }
                    }

                    dict[cid] = new BaleUserInfo
                    {
                        ChatId = cid,
                        BaleUsername = s.Username,
                        DisplayName = name != "-" ? name : (!string.IsNullOrWhiteSpace(s.Username) ? $"@{s.Username}" : $"چت {cid}"),
                        PhoneNumber = phone,
                        NationalId = natId,
                        Source = "کاربر ربات",
                        LastActivityDate = s.LastUpdated
                    };
                }
            }

            return dict.Values
                .OrderByDescending(u => u.LastActivityDate ?? DateTime.MinValue)
                .ToList();
        }

        public async Task<bool> SendUserMessageAsync(string chatId, string text, object? replyMarkup = null, string parseMode = "HTML", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var token = settings.UserBotToken?.Trim();
                if (string.IsNullOrWhiteSpace(token)) return false;

                var client = _httpClientFactory.CreateClient("BaleClient");
                var url = $"https://tapi.bale.ai/bot{token}/sendMessage";

                var effectiveText = text;
                var effectiveParseMode = parseMode;

                // بله از تگ‌های HTML پشتیبانی نمی‌کند و آن‌ها را به صورت متنی خام نشان می‌دهد.
                // به همین دلیل متن‌های دارای تگ یا با parseMode=HTML به مارک‌داون تبدیل می‌شوند.
                if (string.Equals(parseMode, "HTML", StringComparison.OrdinalIgnoreCase) || (text.Contains('<') && text.Contains('>')))
                {
                    effectiveText = ConvertHtmlToMarkdown(text);
                    effectiveParseMode = "Markdown";
                }

                var payload = new Dictionary<string, object>
                {
                    ["chat_id"] = chatId,
                    ["text"] = effectiveText,
                    ["parse_mode"] = effectiveParseMode
                };
                if (replyMarkup != null) payload["reply_markup"] = replyMarkup;

                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content, ct);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                // در صورت خطای بله روی قالب‌بندی مارک‌داون، پیام به صورت متن تمیز و بدون parse_mode باز ارسال می‌شود
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var fallbackPayload = new Dictionary<string, object>
                    {
                        ["chat_id"] = chatId,
                        ["text"] = StripMarkdownAndHtml(text)
                    };
                    if (replyMarkup != null) fallbackPayload["reply_markup"] = replyMarkup;

                    using var fallbackContent = new StringContent(JsonSerializer.Serialize(fallbackPayload), Encoding.UTF8, "application/json");
                    var retryResponse = await client.PostAsync(url, fallbackContent, ct);
                    if (retryResponse.IsSuccessStatusCode) return true;
                }

                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("خطا در ارسال پیام به کاربر بله (وضعیت: {Status}، خطا: {Error})", response.StatusCode, err);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال پیام به کاربر بله (چت: {ChatId})", chatId);
                return false;
            }
        }

        public async Task<bool> SendUserDocumentAsync(string chatId, byte[] fileBytes, string fileName, string? caption = null, object? replyMarkup = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(chatId) || fileBytes == null || fileBytes.Length == 0) return false;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var token = settings.UserBotToken?.Trim();
                if (string.IsNullOrWhiteSpace(token)) return false;

                var client = _httpClientFactory.CreateClient("BaleClient");
                var url = $"https://tapi.bale.ai/bot{token}/sendDocument";

                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(chatId), "chat_id");

                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "document", fileName);

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    var effectiveCaption = caption;
                    if (caption.Contains('<') && caption.Contains('>'))
                    {
                        effectiveCaption = ConvertHtmlToMarkdown(caption);
                    }
                    content.Add(new StringContent(effectiveCaption), "caption");
                }

                if (replyMarkup != null)
                {
                    content.Add(new StringContent(JsonSerializer.Serialize(replyMarkup)), "reply_markup");
                }

                var response = await client.PostAsync(url, content, ct);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("خطا در ارسال سند به بله (وضعیت: {Status}، خطا: {Error})", response.StatusCode, err);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال سند به چت بله {ChatId}", chatId);
                return false;
            }
        }

        public async Task<byte[]?> DownloadFileAsync(string fileId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) return null;

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var token = settings.UserBotToken?.Trim();
                if (string.IsNullOrWhiteSpace(token)) return null;

                var client = _httpClientFactory.CreateClient("BaleClient");
                var getFileUrl = $"https://tapi.bale.ai/bot{token}/getFile?file_id={fileId}";

                var response = await client.GetAsync(getFileUrl, ct);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("result", out var resultEl)) return null;

                string? filePath = null;
                if (resultEl.TryGetProperty("file_path", out var fpEl))
                {
                    filePath = fpEl.GetString();
                }

                if (string.IsNullOrWhiteSpace(filePath)) return null;

                var downloadUrl = filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? filePath
                    : $"https://tapi.bale.ai/file/bot{token}/{filePath}";

                return await client.GetByteArrayAsync(downloadUrl, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در دانلود فایل {FileId} از بله", fileId);
                return null;
            }
        }

        public static string ConvertHtmlToMarkdown(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            var text = html;

            // تبدیل تگ‌های بولد با پشتیبانی از فاصله‌های درون تگ
            text = Regex.Replace(text, @"<\s*(?:b|strong)\s*>\s*(.*?)\s*<\s*/\s*(?:b|strong)\s*>", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<\s*/?\s*(?:b|strong)\s*>", "*", RegexOptions.IgnoreCase);

            // تبدیل تگ‌های ایتالیک با پشتیبانی از فاصله‌های درون تگ
            text = Regex.Replace(text, @"<\s*(?:i|em)\s*>\s*(.*?)\s*<\s*/\s*(?:i|em)\s*>", "_$1_", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<\s*/?\s*(?:i|em)\s*>", "_", RegexOptions.IgnoreCase);

            // تبدیل تگ‌های کد درون‌خطی
            text = Regex.Replace(text, @"<\s*code\s*>\s*(.*?)\s*<\s*/\s*code\s*>", "`$1`", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // تبدیل تگ‌های بلوک کد
            text = Regex.Replace(text, @"<\s*pre\s*>\s*(.*?)\s*<\s*/\s*pre\s*>", "```\n$1\n```", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // تبدیل لینک‌ها
            text = Regex.Replace(text, @"<a\s+href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // تبدیل خط جدید
            text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);

            // حذف سایر تگ‌های متفرقه HTML
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);

            // بازگرداندن کاراکترهای اسکیپ‌شده HTML
            text = WebUtility.HtmlDecode(text);

            return text;
        }

        public static string StripMarkdownAndHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var clean = ConvertHtmlToMarkdown(text);
            clean = clean.Replace("*", "").Replace("`", "");
            return clean;
        }

        private async Task ShowMainMenuAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            var isReseller = user != null && string.Equals(user.Role, "reseller", StringComparison.OrdinalIgnoreCase);
            var userState = await _context.BaleUserStates.FindAsync(new object[] { chatId }, ct);
            var effectiveProfile = await GetEffectiveProfileAsync(user, userState, ct);
            var isRegistered = user != null && (user.Profile != null || effectiveProfile != null);

            var welcome = isRegistered
                ? $"سلام <b>{HtmlSafe(user!.DisplayName)}</b> عزیز، به پنل اختصاصی سامانه مودیان خوش آمدید." +
                  (isReseller && effectiveProfile != null ? $"\n🏢 <b>پروفایل فعال جاری:</b> {HtmlSafe(effectiveProfile.DisplayName)} (حافظه مالیاتی: <code>{effectiveProfile.MemoryId ?? "-"}</code>)" : "") +
                  "\nجهت انجام امور از منوی زیر استفاده فرمایید:"
                : "👋 سلام، به <b>ربات سامانه صدور فاکتور و کارپوشه مودیان</b> خوش آمدید.\n\n" +
                  "جهت دریافت شناسه یکتا و ارسال فاکتور، لطفاً از گزینه‌های زیر استفاده نمایید:";

            await SendUserMessageAsync(chatId, welcome, GetMainUserReplyKeyboard(isRegistered, isReseller), ct: ct);
        }

        private static object GetMainUserReplyKeyboard(bool isRegistered, bool isReseller = false)
        {
            if (isRegistered)
            {
                if (isReseller)
                {
                    return new
                    {
                        keyboard = new[]
                        {
                            new[] { new { text = "📄 ارسال فاکتور جدید" }, new { text = "❌ ابطال فاکتور" } },
                            new[] { new { text = "🏢 مدیریت و انتخاب پروفایل فعال" } },
                            new[] { new { text = "⭐ کالاهای منتخب و جستجوی شناسه" } },
                            new[] { new { text = "💰 کیف پول و موجودی" }, new { text = "⏳ تمدید دوره و اشتراک" } },
                            new[] { new { text = "👤 وضعیت پرونده و شناسه یکتا" }, new { text = "❓ راهنما و پشتیبانی" } }
                        },
                        resize_keyboard = true,
                        one_time_keyboard = false
                    };
                }

                return new
                {
                    keyboard = new[]
                    {
                        new[] { new { text = "📄 ارسال فاکتور جدید" }, new { text = "❌ ابطال فاکتور" } },
                        new[] { new { text = "⭐ کالاهای منتخب و جستجوی شناسه" } },
                        new[] { new { text = "💰 کیف پول و موجودی" }, new { text = "⏳ تمدید دوره و اشتراک" } },
                        new[] { new { text = "👤 وضعیت پرونده و شناسه یکتا" }, new { text = "❓ راهنما و پشتیبانی" } }
                    },
                    resize_keyboard = true,
                    one_time_keyboard = false
                };
            }

            return new
            {
                keyboard = new[]
                {
                    new[] { new { text = "📝 ثبت‌نام و دریافت شناسه یکتا" } },
                    new[] { new { text = "🔗 ورود / اتصال به حساب کاربری" }, new { text = "👤 وضعیت پرونده و شناسه یکتا" } },
                    new[] { new { text = "❓ راهنما و پشتیبانی" } }
                },
                resize_keyboard = true,
                one_time_keyboard = false
            };
        }

        private async Task<Profile?> GetEffectiveProfileAsync(User? user, string? chatId = null, CancellationToken ct = default)
        {
            if (user == null) return null;
            BaleUserState? state = null;
            if (!string.IsNullOrEmpty(chatId))
            {
                state = await _context.BaleUserStates.FindAsync(new object[] { chatId }, ct);
            }
            return await GetEffectiveProfileAsync(user, state, ct);
        }

        private async Task<Profile?> GetEffectiveProfileAsync(User? user, BaleUserState? userState, CancellationToken ct = default)
        {
            if (user == null) return null;

            // اگر کاربر نماینده است
            if (string.Equals(user.Role, "reseller", StringComparison.OrdinalIgnoreCase))
            {
                // ۱. اگر در وضعیت گفتگوی ربات، پروفایل فعالی انتخاب شده باشد
                if (userState?.ActiveProfileId != null)
                {
                    var activeProf = await _context.Profiles.FirstOrDefaultAsync(
                        p => p.Id == userState.ActiveProfileId.Value && (p.Creator == user.Username || p.Id == user.ProfileId), ct);
                    if (activeProf != null)
                        return activeProf;
                }

                // ۲. اگر هنوز انتخابی انجام نشده، اولین پروفایل ساخته‌شده توسط نماینده (مشابه نسخه وب در SelectedProfile)
                var firstSub = await _context.Profiles
                    .Where(p => p.Creator == user.Username && p.Id != user.ProfileId)
                    .OrderBy(p => p.FirstName).ThenBy(p => p.Lastname)
                    .FirstOrDefaultAsync(ct);
                if (firstSub != null)
                    return firstSub;

                // ۳. در غیر این صورت، پروفایل متصل به خود کاربر
                if (user.Profile != null) return user.Profile;
                if (user.ProfileId != null)
                {
                    return await _context.Profiles.FirstOrDefaultAsync(p => p.Id == user.ProfileId.Value, ct);
                }
            }
            else
            {
                // برای کاربر عادی
                if (user.Profile != null) return user.Profile;
                if (user.ProfileId != null)
                {
                    return await _context.Profiles.FirstOrDefaultAsync(p => p.Id == user.ProfileId.Value, ct);
                }
            }

            return null;
        }

        private async Task ShowResellerProfilesAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user == null || !string.Equals(user.Role, "reseller", StringComparison.OrdinalIgnoreCase))
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ این بخش اختصاصی کاربران با نقش «نماینده» (Reseller) می‌باشد.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            var userState = await _context.BaleUserStates.FindAsync(new object[] { chatId }, ct);
            var effectiveProfile = await GetEffectiveProfileAsync(user, userState, ct);
            var activeProfileId = effectiveProfile?.Id;

            var profiles = await _context.Profiles
                .Where(p => p.Creator == user.Username || p.Id == user.ProfileId)
                .OrderBy(p => p.FirstName)
                .ThenBy(p => p.Lastname)
                .ToListAsync(ct);

            if (!profiles.Any())
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ در حال حاضر هیچ پروفایلی تحت مدیریت حساب نمایندگی شما یافت نشد.\nپروفایل‌های ثبت‌شده در وب به صورت خودکار در این لیست نمایش داده خواهند شد.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            var buttons = new List<object[]>();
            foreach (var p in profiles)
            {
                var isActive = p.Id == activeProfileId;
                var label = isActive
                    ? $"✅ {p.DisplayName} (فعال)"
                    : $"🔘 {p.DisplayName}";
                var cbData = isActive ? "noop" : $"reseller_prof:{p.Id}";
                buttons.Add(new[] { new { text = label, callback_data = cbData } });
            }

            buttons.Add(new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } });

            var currentName = effectiveProfile != null ? effectiveProfile.DisplayName : "انتخاب نشده";
            var currentMem = effectiveProfile?.MemoryId ?? "-";
            var currentTin = effectiveProfile?.TinCode ?? "-";

            var msg = "🏢 <b>مدیریت و انتخاب پروفایل فعال نمایندگی</b>\n\n" +
                      $"👤 <b>نماینده:</b> {HtmlSafe(user.Username)}\n" +
                      $"✅ <b>پروفایل فعال کنونی:</b> {HtmlSafe(currentName)}\n" +
                      $"🔑 <b>شناسه حافظه مالیاتی:</b> <code>{currentMem}</code>\n" +
                      $"🆔 <b>کد اقتصادی / ملی:</b> <code>{currentTin}</code>\n\n" +
                      "جهت تغییر پروفایل فعال برای صدور و ارسال فاکتور، روی نام مودی/شرکت مورد نظر در زیر کلیک نمایید:";

            await SendUserMessageAsync(chatId, msg, new { inline_keyboard = buttons.ToArray() }, ct: ct);
        }

        private async Task ShowFavoritesMenuAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user == null)
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ شما هنوز حسابی در سامانه متصل نکرده‌اید. لطفاً ابتدا ثبت‌نام کرده یا وارد حساب خود شوید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            var favorites = await _context.UserFavoriteStuffs
                .Include(f => f.Stuff)
                .Where(f => f.Username == user.Username)
                .OrderByDescending(f => f.AddedDate)
                .Take(15)
                .ToListAsync(ct);

            var stuffCodes = favorites.Select(f => f.StuffCode).ToList();
            var userProds = await _context.Products
                .Where(p => p.Creator == user.Username && stuffCodes.Contains(p.Sstid))
                .ToDictionaryAsync(p => p.Sstid, ct);

            var sb = new StringBuilder();
            sb.AppendLine("⭐ <b>لیست کالاهای منتخب و علاقه‌مندی‌های شما</b>\n");

            if (!favorites.Any())
            {
                sb.AppendLine("هنوز کالایی را به لیست علاقه‌مندی‌های خود اضافه نکرده‌اید.");
                sb.AppendLine("با استفاده از دکمه «🔍 جستجوی شناسه کالا و خدمات»، کالای مورد نظر را جستجو و با یک کلیک به لیست خود اضافه کنید.");
            }
            else
            {
                sb.AppendLine($"تعداد کالاهای منتخب: <b>{favorites.Count} مورد</b>\n");
                int idx = 1;
                foreach (var f in favorites)
                {
                    userProds.TryGetValue(f.StuffCode, out var prod);
                    var title = prod?.Sstt ?? f.Stuff?.Title ?? "بدون شرح";
                    var vat = f.Stuff != null ? $"{f.Stuff.VatRate}%" : (prod != null ? $"{(int)prod.Vra}%" : "-");
                    sb.AppendLine($"{idx}️⃣ <b>{HtmlSafe(title)}</b>");
                    sb.AppendLine($"   شناسه: <code>{f.StuffCode}</code> | مالیات: <code>{vat}</code>");
                    idx++;
                }
            }

            var inlineButtons = new List<object[]>();
            inlineButtons.Add(new[] { new { text = "🔍 جستجوی شناسه کالا و خدمات", callback_data = "fav_search_start" } });

            if (favorites.Any())
            {
                // دکمه‌های حذف برای ۵ کالای اول
                foreach (var f in favorites.Take(5))
                {
                    userProds.TryGetValue(f.StuffCode, out var prod);
                    var shortTitle = prod?.Sstt ?? f.Stuff?.Title ?? f.StuffCode;
                    if (shortTitle.Length > 20) shortTitle = shortTitle.Substring(0, 18) + "...";
                    inlineButtons.Add(new[]
                    {
                        new { text = $"❌ حذف {shortTitle}", callback_data = $"fav_del:{f.StuffCode}" }
                    });
                }
            }

            inlineButtons.Add(new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } });

            await SendUserMessageAsync(chatId, sb.ToString(), new { inline_keyboard = inlineButtons.ToArray() }, ct: ct);
        }

        private static object GetBackToMenuInlineKeyboard()
        {
            return new
            {
                inline_keyboard = new[]
                {
                    new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } }
                }
            };
        }

        private static object GetCancelReplyKeyboard()
        {
            return new
            {
                keyboard = new[]
                {
                    new[] { new { text = "🔙 بازگشت" }, new { text = "❌ انصراف" } }
                },
                resize_keyboard = true,
                one_time_keyboard = false
            };
        }

        private static object GetPhoneReplyKeyboard()
        {
            return new
            {
                keyboard = new object[]
                {
                    new object[] { new { text = "📱 اشتراک‌گذاری شماره همراه من", request_contact = true } },
                    new object[] { new { text = "🔙 بازگشت" }, new { text = "❌ انصراف", request_contact = false } }
                },
                resize_keyboard = true,
                one_time_keyboard = false
            };
        }

        private async Task<BaleUserState> GetOrCreateStateAsync(string chatId, string? username, CancellationToken ct)
        {
            var state = await _context.BaleUserStates.FindAsync(new object[] { chatId }, ct);
            if (state == null)
            {
                state = new BaleUserState
                {
                    ChatId = chatId,
                    Username = username,
                    CurrentStep = "None",
                    LastUpdated = DateTime.Now
                };
                _context.BaleUserStates.Add(state);
                await _context.SaveChangesAsync(ct);
            }
            else
            {
                state.LastUpdated = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(username)) state.Username = username;
            }
            return state;
        }

        #region فاز ۳: مدیریت ارسال و ابطال صورتحساب‌های مالیاتی در پیام‌رسان بله

        private async Task HandleDownloadTemplateAsync(string chatId, CancellationToken ct)
        {
            try
            {
                var templatePath = Path.Combine(_env?.WebRootPath ?? "wwwroot", "template.xlsx");
                byte[] bytes;

                if (System.IO.File.Exists(templatePath))
                {
                    bytes = await System.IO.File.ReadAllBytesAsync(templatePath, ct);
                }
                else
                {
                    // تولید قالب آماده اکسل در حافظه در صورت عدم وجود فایل در دیسک
                    using var ms = new MemoryStream();
                    using (var doc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
                    {
                        var wbPart = doc.AddWorkbookPart();
                        wbPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
                        var wsPart = wbPart.AddNewPart<WorksheetPart>();
                        var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
                        wsPart.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(sheetData);

                        var sheets = doc.WorkbookPart!.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
                        sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
                        {
                            Id = doc.WorkbookPart.GetIdOfPart(wsPart),
                            SheetId = 1,
                            Name = "Invoices"
                        });

                        var headerRow = new DocumentFormat.OpenXml.Spreadsheet.Row();
                        var headers = new[]
                        {
                            "کد فاکتور", "تاریخ فاکتور", "نوع فاکتور", "الگوی فاکتور", "روش پرداخت", "نوع خریدار",
                            "شناسه خریدار", "کد کالا", "شرح کالا", "تعداد", "فی (ریال)", "تخفیف (ریال)", "نرخ مالیات", "واحد سنجش"
                        };
                        foreach (var h in headers)
                        {
                            var cell = new DocumentFormat.OpenXml.Spreadsheet.Cell
                            {
                                DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
                                CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(h)
                            };
                            headerRow.Append(cell);
                        }
                        sheetData.Append(headerRow);
                        doc.Save();
                    }
                    bytes = ms.ToArray();
                }

                await SendUserDocumentAsync(chatId, bytes, "Invoice_Template.xlsx",
                    "📥 <b>قالب نمونه اکسل جهت ارسال گروهی صورتحساب‌ها</b>\n\n" +
                    "لطفاً فاکتورهای خود را طبق ستون‌های این فایل تنظیم نموده و سپس همین فایل را در چت ارسال فرمایید.",
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در دانلود یا ارسال فایل نمونه اکسل فاکتور به چت {ChatId}", chatId);
                await SendUserMessageAsync(chatId, "⚠️ خطا در دریافت فایل نمونه. لطفاً مجدداً تلاش فرمایید.", ct: ct);
            }
        }

        private async Task HandleDocumentMessageAsync(string chatId, JsonElement docEl, CancellationToken ct)
        {
            var fileName = docEl.TryGetProperty("file_name", out var fnProp) ? fnProp.GetString() : "file.xlsx";
            var fileId = docEl.TryGetProperty("file_id", out var fiProp) ? fiProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ لطفاً فایل صورتحساب‌ها را با فرمت اکسل (<code>.xlsx</code>) ارسال فرمایید.\n" +
                    "جهت دریافت قالب نمونه، می‌توانید از دستور «📄 ارسال فاکتور جدید» و دکمه دانلود نمونه استفاده نمایید.",
                    ct: ct);
                return;
            }

            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);

            var effectiveProfile = await GetEffectiveProfileAsync(user, chatId, ct);
            if (user == null || effectiveProfile == null)
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ برای ارسال فاکتور، ابتدا باید در سامانه ثبت‌نام نموده یا وارد حساب کاربری خود شوید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(fileId))
            {
                await SendUserMessageAsync(chatId, "⚠️ امکان دریافت شناسه فایل وجود ندارد. لطفاً مجدداً فایل اکسل را ارسال فرمایید.", ct: ct);
                return;
            }

            await SendUserMessageAsync(chatId, "⏳ در حال دریافت و بررسی فایل اکسل صورتحساب‌ها...", ct: ct);

            var bytes = await DownloadFileAsync(fileId, ct);
            if (bytes == null || bytes.Length == 0)
            {
                await SendUserMessageAsync(chatId, "❌ خطا در دانلود فایل اکسل از پیام‌رسان بله. لطفاً مجدداً ارسال فرمایید.", ct: ct);
                return;
            }

            try
            {
                using var ms = new MemoryStream(bytes);
                var excelDoc = ExcelHelp.Open(ms);
                if (excelDoc.Rows == null || !excelDoc.Rows.Any())
                {
                    await SendUserMessageAsync(chatId, "⚠️ فایل اکسل ارسالی خالی است یا سطرهای داده در آن یافت نشد.", ct: ct);
                    return;
                }

                // حذف پیش‌نویس‌های قبلی معلق کاربر برای جلوگیری از تداخل
                var oldDrafts = await _context.Invoices
                    .Where(i => i.Draft && i.Sender == user.Username)
                    .ToListAsync(ct);
                if (oldDrafts.Any())
                {
                    _context.Invoices.RemoveRange(oldDrafts);
                    await _context.SaveChangesAsync(ct);
                }

                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < excelDoc.ColumnHeaders.Count; c++)
                {
                    var h = (excelDoc.ColumnHeaders[c] ?? "").Trim();
                    if (h.Contains("کد فاکتور") || h.Contains("شماره فاکتور") || h.Equals("invoicecode", StringComparison.OrdinalIgnoreCase)) colMap["Code"] = c;
                    else if (h.Contains("تاریخ") || h.Equals("invoicedate", StringComparison.OrdinalIgnoreCase)) colMap["Date"] = c;
                    else if (h.Contains("نوع فاکتور") || h.Contains("نوع صورتحساب") || h.Equals("invoicetype", StringComparison.OrdinalIgnoreCase)) colMap["Type"] = c;
                    else if (h.Contains("الگو") || h.Equals("invoicepattern", StringComparison.OrdinalIgnoreCase)) colMap["Pattern"] = c;
                    else if (h.Contains("روش") || h.Contains("تسویه") || h.Equals("paymentmethod", StringComparison.OrdinalIgnoreCase)) colMap["Payment"] = c;
                    else if (h.Contains("شخص") || h.Contains("خریدار") && h.Contains("نوع") || h.Equals("persontype", StringComparison.OrdinalIgnoreCase)) colMap["BuyerType"] = c;
                    else if (h.Contains("اقتصادی") || h.Contains("ملی") || h.Equals("buyereconomiccode", StringComparison.OrdinalIgnoreCase)) colMap["BuyerTin"] = c;
                    else if (h.Contains("شناسه کالا") || h.Contains("کد کالا") || h.Equals("productcode", StringComparison.OrdinalIgnoreCase) || h.Equals("sstid", StringComparison.OrdinalIgnoreCase)) colMap["ProductCode"] = c;
                    else if (h.Contains("شرح") || h.Contains("نام کالا") || h.Equals("productdescription", StringComparison.OrdinalIgnoreCase) || h.Equals("sstt", StringComparison.OrdinalIgnoreCase)) colMap["Title"] = c;
                    else if (h.Contains("تعداد") || h.Contains("مقدار") || h.Equals("quantity", StringComparison.OrdinalIgnoreCase) || h.Equals("am", StringComparison.OrdinalIgnoreCase)) colMap["Quantity"] = c;
                    else if (h.Contains("فی") || h.Contains("مبلغ واحد") || h.Equals("unitprice", StringComparison.OrdinalIgnoreCase) || h.Equals("fee", StringComparison.OrdinalIgnoreCase)) colMap["Fee"] = c;
                    else if (h.Contains("تخفیف") || h.Equals("discount", StringComparison.OrdinalIgnoreCase) || h.Equals("dis", StringComparison.OrdinalIgnoreCase)) colMap["Discount"] = c;
                    else if (h.Contains("مالیات") || h.Contains("ارزش افزوده") || h.Equals("taxpercent", StringComparison.OrdinalIgnoreCase) || h.Equals("vra", StringComparison.OrdinalIgnoreCase)) colMap["Vat"] = c;
                    else if (h.Contains("واحد") || h.Equals("unit", StringComparison.OrdinalIgnoreCase) || h.Equals("mu", StringComparison.OrdinalIgnoreCase)) colMap["Unit"] = c;
                }

                // در صورت عدم تطابق نام ستون‌ها، از نگاشت موقعیتی پیش‌فرض استفاده می‌شود
                int getCol(string key, int def) => colMap.TryGetValue(key, out var idx) ? idx : def;
                int cCode = getCol("Code", 0);
                int cDate = getCol("Date", 1);
                int cType = getCol("Type", 2);
                int cPat = getCol("Pattern", 3);
                int cPay = getCol("Payment", 4);
                int cBT = getCol("BuyerType", 5);
                int cTin = getCol("BuyerTin", 6);
                int cProd = getCol("ProductCode", 7);
                int cTitle = getCol("Title", 8);
                int cAm = getCol("Quantity", 9);
                int cFee = getCol("Fee", 10);
                int cDis = getCol("Discount", 11);
                int cVat = getCol("Vat", 12);
                int cUnit = getCol("Unit", 13);

                string getCell(ExcelRow r, int cIdx)
                {
                    if (cIdx >= 0 && cIdx < r.Cells.Count) return (r.Cells[cIdx] ?? "").Trim();
                    return string.Empty;
                }

                var measureUnits = await _context.MeasureUnits.AsNoTracking().ToListAsync(ct);
                var invoiceGroups = new Dictionary<string, Invoices.Models.Invoice>(StringComparer.OrdinalIgnoreCase);
                int totalItemsCount = 0;
                long totalSumAmount = 0;

                for (int rIdx = 0; rIdx < excelDoc.Rows.Count; rIdx++)
                {
                    var row = excelDoc.Rows[rIdx];
                    var rawCode = getCell(row, cCode);
                    var rawProd = getCell(row, cProd);
                    if (string.IsNullOrWhiteSpace(rawCode) && string.IsNullOrWhiteSpace(rawProd)) continue;

                    var invoiceKey = !string.IsNullOrWhiteSpace(rawCode) ? rawCode : $"INV_{rIdx + 1}";

                    if (!invoiceGroups.TryGetValue(invoiceKey, out var currentInv))
                    {
                        var rawDate = getCell(row, cDate);
                        var rawType = getCell(row, cType);
                        var rawPat = getCell(row, cPat);
                        var rawPay = getCell(row, cPay);
                        var rawBT = getCell(row, cBT);
                        var rawTin = getCell(row, cTin);

                        var invType = rawType.Contains("دوم") ? InvoiceType.نوع_دوم : InvoiceType.نوع_اول;
                        var invPat = InvoicePattern.فروش;
                        if (Enum.TryParse<InvoicePattern>(rawPat.Replace(" ", "_"), true, out var pParsed)) invPat = pParsed;

                        var setm = CheckoutType.نقدی;
                        if (rawPay.Contains("نسیه") && rawPay.Contains("نقد")) setm = CheckoutType.نقد_نسیه;
                        else if (rawPay.Contains("نسیه")) setm = CheckoutType.نسیه;

                        PersonType? personType = PersonType.حقیقی;
                        if (rawBT.Contains("حقوقی")) personType = PersonType.حقوقی;
                        else if (rawBT.Contains("مصرف") || invType == InvoiceType.نوع_دوم) personType = null;

                        var nowPersianDate = MyDate.GetPersianDate(DateTime.Now)?.ToShortDateString() ?? DateTime.Now.ToString("yyyy/MM/dd");
                        currentInv = new Invoices.Models.Invoice
                        {
                            Sender = user.Username,
                            Ins = InvoiceTopic.اصلی,
                            Inty = invType,
                            Inp = invPat,
                            InvoiceDate = !string.IsNullOrWhiteSpace(rawDate) ? rawDate : nowPersianDate,
                            Tob = personType,
                            Tinb = Utils.ConvertPersianNumberToEnglish(rawTin),
                            Setm = setm,
                            Draft = true,
                            Status = InvoiceStatus.در_انتظار_ارسال,
                            ProfileId = effectiveProfile.Id,
                            Tins = effectiveProfile.TinCode,
                            Indatim = DateTime.Now
                        };

                        invoiceGroups[invoiceKey] = currentInv;
                        _context.Invoices.Add(currentInv);
                    }

                    // قلم کالا
                    var cleanProdCode = ImportModel.NormalizeStuffCode(rawProd) ?? rawProd;
                    var prodTitle = getCell(row, cTitle);
                    if (string.IsNullOrWhiteSpace(prodTitle)) prodTitle = cleanProdCode;

                    var amStr = Utils.ConvertPersianNumberToEnglish(getCell(row, cAm));
                    decimal.TryParse(amStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var am);
                    if (am <= 0) am = 1;

                    var feeStr = Utils.ConvertPersianNumberToEnglish(getCell(row, cFee));
                    long.TryParse(feeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var fee);

                    var disStr = Utils.ConvertPersianNumberToEnglish(getCell(row, cDis));
                    long.TryParse(disStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dis);

                    var vatRate = ImportModel.ParseVatRate(getCell(row, cVat));
                    var vra = VatRate.درصد_10;
                    if (vatRate == 0) vra = VatRate.معاف;
                    else if (vatRate == 9) vra = VatRate.درصد_9;
                    else if (vatRate == 10) vra = VatRate.درصد_10;
                    else if (vatRate == 16) vra = VatRate.درصد_16;

                    var rawUnit = getCell(row, cUnit).Replace(" ", "");
                    var mu = measureUnits.FirstOrDefault(m => m.Title.Replace(" ", "").Contains(rawUnit))?.Code ?? "162";

                    var item = new InvoiceItem
                    {
                        Sstid = cleanProdCode,
                        Sstt = prodTitle,
                        Am = am,
                        Fee = fee,
                        Dis = dis,
                        Vra = vra,
                        Mu = mu
                    };

                    currentInv.InvoiceItems.Add(item);
                    totalItemsCount++;
                    totalSumAmount += (long)(am * fee);
                }

                if (!invoiceGroups.Any())
                {
                    await SendUserMessageAsync(chatId, "⚠️ هیچ فاکتور معتبری در فایل اکسل شناسایی نشد.", ct: ct);
                    return;
                }

                await _context.SaveChangesAsync(ct);

                var isSubscribed = user.ExpireDate > DateTime.Now;
                var perInvoiceCost = Utils.GetPerInvoiceCost(user, _context);
                var totalCost = isSubscribed ? 0 : invoiceGroups.Count * perInvoiceCost;

                var costText = isSubscribed
                    ? "🎁 <b>رایگان</b> (دارای اشتراک فعال سامانه)"
                    : $"<b>{totalCost:N0} ریال</b> (موجودی فعلی: <b>{user.Vallet:N0} ریال</b>)";

                var summary = "📊 <b>پیش‌نویس فاکتورهای اکسل با موفقیت ایجاد شد</b>\n\n" +
                              $"🏢 <b>پروفایل:</b> {HtmlSafe(effectiveProfile.DisplayName)}\n" +
                              $"📑 <b>تعداد صورتحساب‌ها:</b> <b>{invoiceGroups.Count} فاکتور</b>\n" +
                              $"📦 <b>مجموع اقلام کالا/خدمت:</b> <b>{totalItemsCount} ردیف</b>\n" +
                              $"💰 <b>مجموع مبلغ فاکتورها:</b> <b>{totalSumAmount:N0} ریال</b>\n" +
                              $"💳 <b>هزینه ارسال:</b> {costText}\n\n" +
                              "آیا مایل به تایید نهایی و ارسال به سامانه مودیان هستید؟";

                var buttons = new[]
                {
                    new[] { new { text = "🚀 تایید و ارسال به سامانه مودیان", callback_data = "inv_excel_confirm" } },
                    new[] { new { text = "❌ لغو و حذف پیش‌نویس‌ها", callback_data = "inv_excel_cancel" } }
                };

                await SendUserMessageAsync(chatId, summary, new { inline_keyboard = buttons }, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش فایل اکسل صورتحساب از چت {ChatId}", chatId);
                await SendUserMessageAsync(chatId, "❌ خطا در پردازش فایل اکسل: " + ex.Message, ct: ct);
            }
        }

        private async Task SendExcelDraftInvoicesAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);

            var effectiveProfile = await GetEffectiveProfileAsync(user, chatId, ct);
            if (user == null || effectiveProfile == null)
            {
                await SendUserMessageAsync(chatId, "⚠️ دسترسی نامعتبر است.", ct: ct);
                return;
            }

            var drafts = await _context.Invoices
                .Include(i => i.InvoiceItems)
                .Include(i => i.Payments)
                .Include(i => i.Profile)
                .Where(i => i.Draft && i.Sender == user.Username)
                .ToListAsync(ct);

            if (!drafts.Any())
            {
                await SendUserMessageAsync(chatId, "⚠️ هیچ فاکتور پیش‌نویسی جهت ارسال یافت نشد.", ct: ct);
                await ShowMainMenuAsync(chatId, ct);
                return;
            }

            var isSubscribed = user.ExpireDate > DateTime.Now;
            var perInvoiceCost = Utils.GetPerInvoiceCost(user, _context);
            var totalCost = isSubscribed ? 0 : drafts.Count * perInvoiceCost;

            if (!isSubscribed && user.Vallet < totalCost)
            {
                await SendUserMessageAsync(chatId,
                    $"⚠️ موجودی کیف پول شما جهت ارسال این فاکتورها کافی نیست.\n" +
                    $"• هزینه مورد نیاز: <b>{totalCost:N0} ریال</b>\n" +
                    $"• موجودی کیف پول: <b>{user.Vallet:N0} ریال</b>\n\n" +
                    "لطفاً از طریق منوی «💰 کیف پول و موجودی» حساب خود را شارژ فرمایید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            await SendUserMessageAsync(chatId, $"⏳ در حال امضا و ارسال {drafts.Count} صورتحساب به سامانه مودیان...", ct: ct);

            int successCount = 0;
            int failedCount = 0;
            var sbResults = new StringBuilder();

            foreach (var inv in drafts)
            {
                try
                {
                    var response = _invoiceSender.SendInvoice(inv);
                    inv.Status = response.Status;
                    inv.ReferenceCode = response.RefId;
                    inv.ResponseMessage = response.Response;
                    inv.UID = response.UID;
                    inv.Taxid = response.TaxId;
                    inv.SendDate = DateTime.Now;

                    if (response.Status == InvoiceStatus.موفق)
                    {
                        inv.Draft = false;
                        successCount++;

                        if (!isSubscribed)
                        {
                            user.Vallet -= perInvoiceCost;
                            user.InvoiceSent = true;
                        }
                    }
                    else
                    {
                        failedCount++;
                        inv.Draft = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در ارسال فاکتور پیش‌نویس #{InvoiceId} به مودیان", inv.Id);
                    inv.Status = InvoiceStatus.خطا_در_ارسال;
                    inv.Draft = false;
                    failedCount++;
                }
            }

            await _context.SaveChangesAsync(ct);

            await _activityLogger.LogAsync("بله", "ارسال گروهی فاکتور اکسل",
                $"ارسال گروهی {drafts.Count} فاکتور توسط کاربر {user.Username} ({successCount} موفق، {failedCount} ناموفق)",
                entityId: successCount.ToString(), ct: ct);

            var reportMsg = $"🏁 <b>گزارش ارسال فاکتورها به سامانه مودیان</b>\n\n" +
                            $"✅ صورتحساب‌های موفق: <b>{successCount} عدد</b>\n" +
                            $"❌ صورتحساب‌های ناموفق: <b>{failedCount} عدد</b>\n" +
                            $"💰 موجودی باقیمانده کیف پول: <b>{user.Vallet:N0} ریال</b>\n\n" +
                            "می‌توانید وضعیت و استعلام هر فاکتور را از طریق پنل وب یا پشتیبانی پیگیری نمایید.";

            await SendUserMessageAsync(chatId, reportMsg, ct: ct);
            await ShowMainMenuAsync(chatId, ct);
        }

        private async Task CancelExcelDraftsAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user != null)
            {
                var drafts = await _context.Invoices
                    .Where(i => i.Draft && i.Sender == user.Username)
                    .ToListAsync(ct);
                if (drafts.Any())
                {
                    _context.Invoices.RemoveRange(drafts);
                    await _context.SaveChangesAsync(ct);
                }
            }

            await SendUserMessageAsync(chatId, "🗑️ کلیه پیش‌نویس‌های فاکتور اکسل لغو و پاکسازی شدند.", ct: ct);
            await ShowMainMenuAsync(chatId, ct);
        }

        private async Task StartInvoiceWizardAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);

            var effectiveProfile = await GetEffectiveProfileAsync(user, userState, ct);
            if (user == null || effectiveProfile == null)
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ برای صدور فاکتور، ابتدا باید در سامانه ثبت‌نام نموده یا وارد حساب کاربری خود شوید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            userState.CurrentStep = "InvWiz_BuyerType";
            userState.StateDataJson = JsonSerializer.Serialize(new Dictionary<string, string>());
            await _context.SaveChangesAsync(ct);

            var prompt = "📄 <b>مرحله ۱ از ۵: انتخاب نوع خریدار و صورتحساب</b>\n\n" +
                         $"🏢 <b>پروفایل صادرکننده:</b> {HtmlSafe(effectiveProfile.DisplayName)}\n" +
                         $"🔑 <b>شناسه حافظه:</b> <code>{effectiveProfile.MemoryId ?? "-"}</code>\n\n" +
                         "لطفاً نوع خریدار صورتحساب را انتخاب فرمایید:";

            var buttons = new[]
            {
                new[] { new { text = "👤 مصرف‌کننده نهایی (نوع ۲ - الگوی اول)", callback_data = "inv_wiz_bt:consumer" } },
                new[] { new { text = "🧑 شخص حقیقی با کد ملی (نوع ۱)", callback_data = "inv_wiz_bt:individual" } },
                new[] { new { text = "🏢 شخص حقوقی / شرکت (نوع ۱)", callback_data = "inv_wiz_bt:legal" } },
                new[] { new { text = "🔙 بازگشت به منوی اصلی", callback_data = "main_menu" } }
            };

            await SendUserMessageAsync(chatId, prompt, new { inline_keyboard = buttons }, ct: ct);
        }

        private async Task HandleBuyerTypeSelectedAsync(string chatId, BaleUserState userState, string buyerType, CancellationToken ct)
        {
            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            data["BuyerType"] = buyerType;

            if (buyerType == "consumer")
            {
                data["BuyerId"] = "";
                userState.CurrentStep = "InvWiz_ItemSelect";
                userState.StateDataJson = JsonSerializer.Serialize(data);
                await _context.SaveChangesAsync(ct);

                await ShowItemSelectMenuAsync(chatId, userState, ct);
            }
            else
            {
                userState.CurrentStep = "InvWiz_BuyerId";
                userState.StateDataJson = JsonSerializer.Serialize(data);
                await _context.SaveChangesAsync(ct);

                var prompt = buyerType == "legal"
                    ? "🏢 <b>مرحله ۲ از ۵: شناسه ملی شرکت / شخص حقوقی</b>\n\nلطفاً <b>شناسه ملی ۱۱ رقمی</b> خریدار حقوقی را وارد نمایید:"
                    : "📝 <b>مرحله ۲ از ۵: کد ملی خریدار حقیقی</b>\n\nلطفاً <b>کد ملی ۱۰ رقمی</b> خریدار را وارد فرمایید:";

                await SendUserMessageAsync(chatId, prompt, GetCancelReplyKeyboard(), ct: ct);
            }
        }

        private async Task ShowItemSelectMenuAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user == null) return;

            var favorites = await _context.UserFavoriteStuffs
                .Include(f => f.Stuff)
                .Where(f => f.Username == user.Username)
                .OrderByDescending(f => f.AddedDate)
                .Take(6)
                .ToListAsync(ct);

            var stuffCodes = favorites.Select(f => f.StuffCode).ToList();
            var userProds = await _context.Products
                .Where(p => p.Creator == user.Username && stuffCodes.Contains(p.Sstid))
                .ToDictionaryAsync(p => p.Sstid, ct);

            var buttons = new List<object[]>();

            foreach (var f in favorites)
            {
                userProds.TryGetValue(f.StuffCode, out var prod);
                var title = prod?.Sstt ?? f.Stuff?.Title ?? f.StuffCode;
                if (title.Length > 24) title = title.Substring(0, 22) + "...";
                var vat = f.Stuff != null ? $"{f.Stuff.VatRate}%" : (prod != null ? $"{(int)prod.Vra}%" : "10%");
                buttons.Add(new[]
                {
                    new { text = $"📦 {title} ({vat})", callback_data = $"inv_wiz_item:{f.StuffCode}" }
                });
            }

            buttons.Add(new[] { new { text = "🔍 جستجو یا ورود شناسه ۱۳ رقمی", callback_data = "inv_wiz_item_search_btn" } });
            buttons.Add(new[] { new { text = "🔙 بازگشت", callback_data = "inv_wiz_back" }, new { text = "❌ انصراف", callback_data = "inv_wiz_cancel" } });

            var msg = "📦 <b>مرحله ۳ از ۵: انتخاب کالا یا خدمت</b>\n\n" +
                      "می‌توانید یکی از کالاهای منتخب زیر را با یک لمس انتخاب کنید یا از دکمه جستجو جهت یافتن سایر شناسه‌ها استفاده نمایید:";

            await SendUserMessageAsync(chatId, msg, new { inline_keyboard = buttons.ToArray() }, ct: ct);
        }

        private async Task SelectItemByCodeAsync(string chatId, BaleUserState userState, string code, CancellationToken ct)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user == null) return;

            var cleanCode = ImportModel.NormalizeStuffCode(code) ?? code;
            var stuff = await _context.Stuffs.FindAsync(new object[] { cleanCode }, ct);
            var prod = await _context.Products.FirstOrDefaultAsync(p => p.Sstid == cleanCode && p.Creator == user.Username, ct);

            var title = prod?.Sstt ?? stuff?.Title ?? cleanCode;
            int vatRate = stuff?.VatRate ?? (prod != null ? (int)prod.Vra : 10);
            long defaultFee = (long)(prod?.Fee ?? 0);
            string mu = prod?.Mu ?? "162";

            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            data["ItemCode"] = cleanCode;
            data["ItemTitle"] = title;
            data["ItemVat"] = vatRate.ToString(CultureInfo.InvariantCulture);
            data["DefaultFee"] = defaultFee.ToString(CultureInfo.InvariantCulture);
            data["ItemMu"] = mu;

            userState.CurrentStep = "InvWiz_Quantity";
            userState.StateDataJson = JsonSerializer.Serialize(data);
            await _context.SaveChangesAsync(ct);

            var msg = $"📦 <b>کالای انتخابی:</b> {HtmlSafe(title)}\n" +
                      $"شناسه: <code>{cleanCode}</code> | نرخ مالیات ارزش افزوده: <b>{vatRate}%</b>\n\n" +
                      "🔢 لطفاً <b>تعداد یا مقدار</b> را به صورت عدد وارد فرمایید (مثال: <code>1</code> یا <code>2.5</code>):";

            await SendUserMessageAsync(chatId, msg, GetCancelReplyKeyboard(), ct: ct);
        }

        private async Task ProcessItemSearchInputAsync(string chatId, BaleUserState userState, string? text, CancellationToken ct)
        {
            var clean = (text ?? "").Trim();
            var cleanCode = ImportModel.NormalizeStuffCode(clean);

            if (!string.IsNullOrWhiteSpace(cleanCode) && cleanCode.Length == 13 && cleanCode.All(char.IsDigit))
            {
                await SelectItemByCodeAsync(chatId, userState, cleanCode, ct);
                return;
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            var prodMatches = user != null
                ? await _context.Products
                    .Where(p => p.Creator == user.Username && (p.Sstid.Contains(clean) || (p.Sstt != null && p.Sstt.Contains(clean))))
                    .Take(4)
                    .ToListAsync(ct)
                : new List<Product>();

            var stuffMatches = await _context.Stuffs
                .Where(s => s.Code.Contains(clean) || s.Title.Contains(clean))
                .Take(6)
                .ToListAsync(ct);

            if (!prodMatches.Any() && !stuffMatches.Any())
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ موردی با این مشخصات یافت نشد.\nلطفاً شناسه ۱۳ رقمی را به صورت دقیق وارد فرمایید یا عبارت دیگری را جستجو نمایید:",
                    GetCancelReplyKeyboard(),
                    ct: ct);
                return;
            }

            var buttons = new List<object[]>();
            var seenCodes = new HashSet<string>();

            foreach (var p in prodMatches)
            {
                if (string.IsNullOrWhiteSpace(p.Sstid) || seenCodes.Contains(p.Sstid)) continue;
                seenCodes.Add(p.Sstid);
                var title = p.Sstt ?? p.Sstid;
                if (title.Length > 24) title = title.Substring(0, 22) + "...";
                buttons.Add(new[]
                {
                    new { text = $"📦 {title} ({(int)p.Vra}%)", callback_data = $"inv_wiz_item:{p.Sstid}" }
                });
            }

            foreach (var m in stuffMatches)
            {
                if (seenCodes.Contains(m.Code)) continue;
                seenCodes.Add(m.Code);
                var title = m.Title;
                if (title.Length > 24) title = title.Substring(0, 22) + "...";
                buttons.Add(new[]
                {
                    new { text = $"📦 {title} ({m.VatRate}%)", callback_data = $"inv_wiz_item:{m.Code}" }
                });
            }

            buttons.Add(new[] { new { text = "🔙 بازگشت", callback_data = "inv_wiz_back" } });

            await SendUserMessageAsync(chatId,
                "🔍 <b>نتایج جستجوی کالا و خدمت:</b>\nجهت انتخاب روی کالا کلیک فرمایید:",
                new { inline_keyboard = buttons.ToArray() },
                ct: ct);
        }

        private async Task ShowFeePromptAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            var defaultFeeStr = data.TryGetValue("DefaultFee", out var df) ? df : "0";
            long.TryParse(defaultFeeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var defaultFee);

            var itemTitle = data.TryGetValue("ItemTitle", out var it) ? it : "-";
            var am = data.TryGetValue("Am", out var a) ? a : "1";

            var msg = $"📦 <b>کالا:</b> {HtmlSafe(itemTitle)} (تعداد: <b>{am}</b>)\n\n" +
                      "💰 لطفاً <b>مبلغ واحد (فی)</b> را به ریال وارد نمایید (بدون کاما، مثال: <code>500000</code>):";

            if (defaultFee > 0)
            {
                var buttons = new[]
                {
                    new[] { new { text = $"✅ تایید فی پیش‌فرض ({defaultFee:N0} ریال)", callback_data = $"inv_wiz_fee_default:{defaultFee}" } },
                    new[] { new { text = "🔙 بازگشت", callback_data = "inv_wiz_back" } }
                };
                await SendUserMessageAsync(chatId, msg, new { inline_keyboard = buttons }, ct: ct);
            }
            else
            {
                await SendUserMessageAsync(chatId, msg, GetCancelReplyKeyboard(), ct: ct);
            }
        }

        private async Task HandleDefaultFeeSelectedAsync(string chatId, BaleUserState userState, string feeVal, CancellationToken ct)
        {
            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            data["Fee"] = feeVal;

            if (data.TryGetValue("BuyerType", out var bt) && bt == "consumer")
            {
                data["CheckoutType"] = "1"; // نقدی
                userState.CurrentStep = "InvWiz_Confirm";
                userState.StateDataJson = JsonSerializer.Serialize(data);
                await _context.SaveChangesAsync(ct);

                await ShowInvoiceWizardConfirmCardAsync(chatId, userState, ct);
            }
            else
            {
                userState.CurrentStep = "InvWiz_CheckoutType";
                userState.StateDataJson = JsonSerializer.Serialize(data);
                await _context.SaveChangesAsync(ct);

                await ShowCheckoutTypeMenuAsync(chatId, userState, ct);
            }
        }

        private async Task ShowCheckoutTypeMenuAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            var prompt = "💳 <b>مرحله ۴ از ۵: انتخاب روش تسویه حساب</b>\n\n" +
                         "لطفاً نحوه پرداخت و تسویه صورتحساب را انتخاب فرمایید:";

            var buttons = new[]
            {
                new[] { new { text = "💵 نقدی", callback_data = "inv_wiz_setm:1" } },
                new[] { new { text = "📝 نسیه", callback_data = "inv_wiz_setm:2" } },
                new[] { new { text = "💳 نقد و نسیه", callback_data = "inv_wiz_setm:3" } },
                new[] { new { text = "🔙 بازگشت", callback_data = "inv_wiz_back" } }
            };

            await SendUserMessageAsync(chatId, prompt, new { inline_keyboard = buttons }, ct: ct);
        }

        private async Task HandleCheckoutTypeSelectedAsync(string chatId, BaleUserState userState, string checkoutType, CancellationToken ct)
        {
            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            data["CheckoutType"] = checkoutType;
            userState.CurrentStep = "InvWiz_Confirm";
            userState.StateDataJson = JsonSerializer.Serialize(data);
            await _context.SaveChangesAsync(ct);

            await ShowInvoiceWizardConfirmCardAsync(chatId, userState, ct);
        }

        private async Task ShowInvoiceWizardConfirmCardAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);

            var effectiveProfile = await GetEffectiveProfileAsync(user, userState, ct);
            if (user == null || effectiveProfile == null) return;

            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            var buyerType = data.TryGetValue("BuyerType", out var bt) ? bt : "consumer";
            var buyerId = data.TryGetValue("BuyerId", out var bi) ? bi : "-";
            var itemTitle = data.TryGetValue("ItemTitle", out var it) ? it : "-";
            var itemCode = data.TryGetValue("ItemCode", out var ic) ? ic : "-";

            decimal.TryParse(data.TryGetValue("Am", out var aStr) ? aStr : "1", NumberStyles.Any, CultureInfo.InvariantCulture, out var am);
            long.TryParse(data.TryGetValue("Fee", out var fStr) ? fStr : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var fee);
            int.TryParse(data.TryGetValue("ItemVat", out var vStr) ? vStr : "10", NumberStyles.Any, CultureInfo.InvariantCulture, out var vatRate);
            var setmStr = data.TryGetValue("CheckoutType", out var sStr) ? sStr : "1";

            long subTotal = (long)(am * fee);
            long vatAmount = (long)(subTotal * vatRate / 100.0);
            long grandTotal = subTotal + vatAmount;

            var buyerDesc = buyerType switch
            {
                "legal" => $"شخص حقوقی (شناسه ملی: <code>{buyerId}</code>)",
                "individual" => $"شخص حقیقی (کد ملی: <code>{buyerId}</code>)",
                _ => "مصرف‌کننده نهایی (نوع دوم)"
            };

            var setmDesc = setmStr switch
            {
                "2" => "نسیه",
                "3" => "نقد و نسیه",
                _ => "نقدی"
            };

            var isSubscribed = user.ExpireDate > DateTime.Now;
            var perInvoiceCost = Utils.GetPerInvoiceCost(user, _context);
            var costDesc = isSubscribed ? "🎁 رایگان (اشتراک فعال)" : $"{perInvoiceCost:N0} ریال";

            var card = "📋 <b>مرحله ۵ از ۵: پیش‌نمایش و تایید نهایی صورتحساب</b>\n\n" +
                       $"🏢 <b>صادرکننده:</b> {HtmlSafe(effectiveProfile.DisplayName)}\n" +
                       $"👤 <b>خریدار:</b> {buyerDesc}\n" +
                       $"📦 <b>کالا/خدمت:</b> {HtmlSafe(itemTitle)}\n" +
                       $"🔢 <b>تعداد/مقدار:</b> <b>{am}</b> | <b>فی:</b> <b>{fee:N0} ریال</b>\n" +
                       $"💵 <b>مبلغ کل کالا:</b> {subTotal:N0} ریال\n" +
                       $"📊 <b>مالیات ارزش افزوده ({vatRate}%):</b> {vatAmount:N0} ریال\n" +
                       $"💎 <b>مبلغ قابل پرداخت:</b> <b>{grandTotal:N0} ریال</b>\n" +
                       $"💳 <b>روش تسویه:</b> {setmDesc}\n" +
                       $"🏷️ <b>هزینه ارسال:</b> {costDesc}\n\n" +
                       "جهت امضا و ارسال مستقیم به سامانه مودیان، دکمه تایید را لمس نمایید:";

            var buttons = new[]
            {
                new[] { new { text = "🚀 تایید و ارسال به سامانه مودیان", callback_data = "inv_wiz_send" } },
                new[] { new { text = "🔙 ویرایش و بازگشت", callback_data = "inv_wiz_back" } },
                new[] { new { text = "❌ انصراف", callback_data = "inv_wiz_cancel" } }
            };

            await SendUserMessageAsync(chatId, card, new { inline_keyboard = buttons }, ct: ct);
        }

        private async Task SendSingleInvoiceWizardAsync(string chatId, BaleUserState userState, CancellationToken ct)
        {
            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);

            var effectiveProfile = await GetEffectiveProfileAsync(user, userState, ct);
            if (user == null || effectiveProfile == null)
            {
                await SendUserMessageAsync(chatId, "⚠️ دسترسی نامعتبر است.", ct: ct);
                return;
            }

            var data = string.IsNullOrWhiteSpace(userState.StateDataJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(userState.StateDataJson) ?? new Dictionary<string, string>();

            var buyerType = data.TryGetValue("BuyerType", out var bt) ? bt : "consumer";
            var buyerId = data.TryGetValue("BuyerId", out var bi) ? bi : null;
            var itemTitle = data.TryGetValue("ItemTitle", out var it) ? it : "-";
            var itemCode = data.TryGetValue("ItemCode", out var ic) ? ic : "-";
            var mu = data.TryGetValue("ItemMu", out var m) ? m : "162";

            decimal.TryParse(data.TryGetValue("Am", out var aStr) ? aStr : "1", NumberStyles.Any, CultureInfo.InvariantCulture, out var am);
            long.TryParse(data.TryGetValue("Fee", out var fStr) ? fStr : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var fee);
            int.TryParse(data.TryGetValue("ItemVat", out var vStr) ? vStr : "10", NumberStyles.Any, CultureInfo.InvariantCulture, out var vatRate);
            var setmStr = data.TryGetValue("CheckoutType", out var sStr) ? sStr : "1";

            var isSubscribed = user.ExpireDate > DateTime.Now;
            var perInvoiceCost = Utils.GetPerInvoiceCost(user, _context);

            if (!isSubscribed && user.Vallet < perInvoiceCost)
            {
                await SendUserMessageAsync(chatId,
                    $"⚠️ موجودی کیف پول شما جهت ارسال این فاکتور کافی نیست.\n" +
                    $"• هزینه فاکتور: <b>{perInvoiceCost:N0} ریال</b>\n" +
                    $"• موجودی شما: <b>{user.Vallet:N0} ریال</b>\n\n" +
                    "لطفاً از طریق گزینه «💰 کیف پول و موجودی» حساب خود را شارژ فرمایید.",
                    GetBackToMenuInlineKeyboard(),
                    ct: ct);
                return;
            }

            await SendUserMessageAsync(chatId, "⏳ در حال امضای الکترونیکی و ارسال فاکتور به سامانه مودیان...", ct: ct);

            var vra = VatRate.درصد_10;
            if (vatRate == 0) vra = VatRate.معاف;
            else if (vatRate == 9) vra = VatRate.درصد_9;
            else if (vatRate == 10) vra = VatRate.درصد_10;
            else if (vatRate == 16) vra = VatRate.درصد_16;

            var setm = setmStr switch
            {
                "2" => CheckoutType.نسیه,
                "3" => CheckoutType.نقد_نسیه,
                _ => CheckoutType.نقدی
            };

            PersonType? tob = buyerType switch
            {
                "legal" => PersonType.حقوقی,
                "individual" => PersonType.حقیقی,
                _ => null
            };

            var nowPersian = MyDate.GetPersianDate(DateTime.Now)?.ToShortDateString() ?? DateTime.Now.ToString("yyyy/MM/dd");
            var invoice = new Invoices.Models.Invoice
            {
                Sender = user.Username,
                Ins = InvoiceTopic.اصلی,
                Inty = buyerType == "consumer" ? InvoiceType.نوع_دوم : InvoiceType.نوع_اول,
                Inp = InvoicePattern.فروش,
                InvoiceDate = nowPersian,
                Tob = tob,
                Tinb = Utils.ConvertPersianNumberToEnglish(buyerId),
                Setm = setm,
                Draft = false,
                Status = InvoiceStatus.در_حال_ارسال,
                ProfileId = effectiveProfile.Id,
                Tins = effectiveProfile.TinCode,
                Indatim = DateTime.Now
            };

            invoice.InvoiceItems.Add(new InvoiceItem
            {
                Sstid = itemCode,
                Sstt = itemTitle,
                Am = am,
                Fee = fee,
                Dis = 0,
                Vra = vra,
                Mu = mu
            });

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(ct);

            try
            {
                var response = _invoiceSender.SendInvoice(invoice);
                invoice.Status = response.Status;
                invoice.ReferenceCode = response.RefId;
                invoice.ResponseMessage = response.Response;
                invoice.UID = response.UID;
                invoice.Taxid = response.TaxId;
                invoice.SendDate = DateTime.Now;

                if (response.Status == InvoiceStatus.موفق)
                {
                    if (!isSubscribed)
                    {
                        user.Vallet -= perInvoiceCost;
                        user.InvoiceSent = true;
                    }

                    await _context.SaveChangesAsync(ct);
                    await _activityLogger.LogInvoiceActionAsync("ارسال فاکتور از بله", invoice.Id, invoice.Taxid, $"صدور موفق فاکتور تکی در ربات بله توسط {user.Username}");

                    userState.CurrentStep = "None";
                    userState.StateDataJson = null;
                    await _context.SaveChangesAsync(ct);

                    var successMsg = "🎉 <b>صورتحساب شما با موفقیت به سامانه مودیان ارسال شد!</b>\n\n" +
                                     $"• شماره منحصر به فرد مالیاتی (TaxID):\n<code>{invoice.Taxid}</code>\n\n" +
                                     $"• کد رهگیری استعلام: <code>{invoice.ReferenceCode ?? invoice.UID ?? "-"}</code>\n" +
                                     $"• مبلغ کل صورتحساب: <b>{(am * fee) + ((am * fee) * vatRate / 100):N0} ریال</b>\n" +
                                     $"• موجودی فعلی کیف پول: <b>{user.Vallet:N0} ریال</b>";

                    await SendUserMessageAsync(chatId, successMsg, ct: ct);
                    await ShowMainMenuAsync(chatId, ct);
                }
                else
                {
                    await _context.SaveChangesAsync(ct);
                    var errMsg = $"❌ <b>خطا در ارسال صورتحساب به سامانه مودیان</b>\n\n" +
                                 $"علت خطا: {HtmlSafe(response.Response ?? "خطای ارتباط با سرور امور مالیاتی")}\n\n" +
                                 "مبلغی از کیف پول شما کسر نگردید. فاکتور به عنوان پیش‌نویس در پنل وب شما ذخیره شد.";
                    await SendUserMessageAsync(chatId, errMsg, ct: ct);
                    await ShowMainMenuAsync(chatId, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "استثنا در ارسال فاکتور تکی ویزارد به مودیان");
                invoice.Status = InvoiceStatus.خطا_در_ارسال;
                await _context.SaveChangesAsync(ct);

                await SendUserMessageAsync(chatId, "❌ خطا در برقراری ارتباط با سامانه مودیان: " + ex.Message, ct: ct);
                await ShowMainMenuAsync(chatId, ct);
            }
        }

        private async Task ShowCancelInvoiceConfirmAsync(string chatId, int invId, CancellationToken ct)
        {
            var invoice = await _context.Invoices
                .Include(i => i.InvoiceItems)
                .Include(i => i.Profile)
                .FirstOrDefaultAsync(i => i.Id == invId, ct);

            if (invoice == null || string.IsNullOrWhiteSpace(invoice.Taxid))
            {
                await SendUserMessageAsync(chatId, "⚠️ فاکتور مورد نظر یافت نشد یا شماره مالیاتی ندارد.", ct: ct);
                return;
            }

            var sumTotal = invoice.InvoiceItems.Sum(item => (long)(item.Am * item.Fee));

            var card = "⚠️ <b>تایید نهایی ابطال صورتحساب الکترونیکی</b>\n\n" +
                       $"• شماره منحصر به فرد مالیاتی:\n<code>{invoice.Taxid}</code>\n\n" +
                       $"• تاریخ فاکتور: <b>{invoice.InvoiceDate ?? "-"}</b>\n" +
                       $"• مجموع مبلغ: <b>{sumTotal:N0} ریال</b>\n" +
                       $"• خریدار: {HtmlSafe(invoice.Tinb ?? "مصرف‌کننده نهایی")}\n\n" +
                       "📌 <i>نکته: فرآیند ابطال فاکتور کاملاً رایگان است و هیچ هزینه‌ای از کیف پول شما کسر نخواهد شد.</i>\n\n" +
                       "آیا از ابطال این صورتحساب اطمینان دارید؟";

            var buttons = new[]
            {
                new[] { new { text = "❌ تایید نهایی ابطال فاکتور", callback_data = $"inv_cancel_confirm:{invoice.Id}" } },
                new[] { new { text = "🔙 انصراف و بازگشت", callback_data = "main_menu" } }
            };

            await SendUserMessageAsync(chatId, card, new { inline_keyboard = buttons }, ct: ct);
        }

        private async Task ShowCancelTaxIdConfirmAsync(string chatId, string taxId, CancellationToken ct)
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Taxid == taxId, ct);

            var card = "⚠️ <b>تایید نهایی ابطال با شناسه مالیاتی</b>\n\n" +
                       $"• شماره مالیاتی مرجع جهت ابطال:\n<code>{taxId}</code>\n\n" +
                       (invoice != null ? $"• تاریخ ثبت اولیه: <b>{invoice.InvoiceDate ?? "-"}</b>\n" : "") +
                       "📌 <i>نکته: فرآیند ابطال فاکتور کاملاً رایگان است و هیچ هزینه‌ای از کیف پول کسر نمی‌گردد.</i>\n\n" +
                       "آیا مایل به ارسال صورتحساب ابطالی برای این شماره به سامانه مودیان هستید؟";

            var buttons = new[]
            {
                new[] { new { text = "❌ تایید نهایی ابطال فاکتور", callback_data = $"inv_cancel_taxid_confirm:{taxId}" } },
                new[] { new { text = "🔙 انصراف و بازگشت", callback_data = "main_menu" } }
            };

            await SendUserMessageAsync(chatId, card, new { inline_keyboard = buttons }, ct: ct);
        }

        private async Task ExecuteInvoiceCancellationAsync(string chatId, int? invoiceId, string? manualTaxId, CancellationToken ct)
        {
            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);

            var effectiveProfile = await GetEffectiveProfileAsync(user, chatId, ct);
            if (user == null || effectiveProfile == null)
            {
                await SendUserMessageAsync(chatId, "⚠️ دسترسی نامعتبر است.", ct: ct);
                return;
            }

            Invoices.Models.Invoice? refInvoice = null;
            string targetTaxId = "";

            if (invoiceId.HasValue)
            {
                refInvoice = await _context.Invoices.FindAsync(new object[] { invoiceId.Value }, ct);
                if (refInvoice != null) targetTaxId = refInvoice.Taxid ?? "";
            }
            else if (!string.IsNullOrWhiteSpace(manualTaxId))
            {
                targetTaxId = manualTaxId.Trim().ToUpperInvariant();
                refInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Taxid == targetTaxId, ct);
            }

            if (string.IsNullOrWhiteSpace(targetTaxId))
            {
                await SendUserMessageAsync(chatId, "⚠️ شناسه مالیاتی فاکتور مرجع مشخص نشده است.", ct: ct);
                return;
            }

            await SendUserMessageAsync(chatId, "⏳ در حال صدور و ارسال صورتحساب ابطالی به سامانه مودیان...", ct: ct);

            var cancelProfId = refInvoice?.ProfileId ?? effectiveProfile.Id;
            var cancelTins = (!string.IsNullOrWhiteSpace(refInvoice?.Tins) ? refInvoice.Tins : effectiveProfile.TinCode);

            var cancelInvoice = new Invoices.Models.Invoice
            {
                Ins = InvoiceTopic.ابطالی,
                Irtaxid = targetTaxId,
                Tins = cancelTins,
                Sender = user.Username,
                Indatim = DateTime.Now,
                ProfileId = cancelProfId,
                InvoiceDate = MyDate.GetPersianDate(DateTime.Now)?.ToShortDateString() ?? DateTime.Now.ToString("yyyy/MM/dd"),
                Status = InvoiceStatus.در_حال_ارسال,
                Draft = false
            };

            _context.Invoices.Add(cancelInvoice);
            await _context.SaveChangesAsync(ct);

            try
            {
                var response = _invoiceSender.SendInvoice(cancelInvoice);
                cancelInvoice.Status = response.Status;
                cancelInvoice.ReferenceCode = response.RefId;
                cancelInvoice.ResponseMessage = response.Response;
                cancelInvoice.UID = response.UID;
                cancelInvoice.Taxid = response.TaxId;
                cancelInvoice.SendDate = DateTime.Now;

                if (response.Status == InvoiceStatus.موفق)
                {
                    if (refInvoice != null)
                    {
                        refInvoice.Status = InvoiceStatus.ابطال_شده;
                        refInvoice.ReferalId = cancelInvoice.Id;
                    }

                    await _context.SaveChangesAsync(ct);
                    await _activityLogger.LogInvoiceActionAsync("ابطال فاکتور از بله", cancelInvoice.Id, cancelInvoice.Taxid, $"ابطال فاکتور مرجع {targetTaxId} توسط {user.Username} در بله");

                    var successMsg = "🎉 <b>صورتحساب ابطالی با موفقیت در سامانه مودیان ثبت گردید!</b>\n\n" +
                                     $"• شناسه مالیاتی ابطالی:\n<code>{cancelInvoice.Taxid}</code>\n\n" +
                                     $"• کد رهگیری: <code>{cancelInvoice.ReferenceCode ?? cancelInvoice.UID ?? "-"}</code>\n" +
                                     $"• فاکتور مرجع: <code>{targetTaxId}</code>\n" +
                                     $"• وضعیت فاکتور مرجع: <b>ابطال‌شده</b>\n" +
                                     "• هزینه کسرشده: <b>۰ ریال (رایگان)</b>";

                    await SendUserMessageAsync(chatId, successMsg, ct: ct);
                    await ShowMainMenuAsync(chatId, ct);
                }
                else
                {
                    await _context.SaveChangesAsync(ct);
                    var errMsg = $"❌ <b>خطا در ابطال صورتحساب</b>\n\n" +
                                 $"علت خطا: {HtmlSafe(response.Response ?? "پاسخ ناموفق از سامانه مودیان")}\n\n" +
                                 "لطفاً از صحت شماره مالیاتی اطمینان حاصل کرده و مجدداً تلاش فرمایید.";
                    await SendUserMessageAsync(chatId, errMsg, ct: ct);
                    await ShowMainMenuAsync(chatId, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "استثنا در ارسال صورتحساب ابطالی به مودیان");
                cancelInvoice.Status = InvoiceStatus.خطا_در_ارسال;
                await _context.SaveChangesAsync(ct);

                await SendUserMessageAsync(chatId, "❌ خطا در ارسال فاکتور ابطالی: " + ex.Message, ct: ct);
                await ShowMainMenuAsync(chatId, ct);
            }
        }

        #endregion
    }
}
