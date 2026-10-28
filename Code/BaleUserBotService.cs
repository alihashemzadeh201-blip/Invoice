using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Models;
using Invoices.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

        public BaleUserBotService(
            DataContext context,
            IBaleSettingsService settingsService,
            IBaleNotificationService adminNotificationService,
            IHttpClientFactory httpClientFactory,
            IUserActivityLogger activityLogger,
            ILogger<BaleUserBotService> logger)
        {
            _context = context;
            _settingsService = settingsService;
            _adminNotificationService = adminNotificationService;
            _httpClientFactory = httpClientFactory;
            _activityLogger = activityLogger;
            _logger = logger;
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

            if (string.Equals(text, "📄 ارسال فاکتور جدید", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "❌ ابطال فاکتور", StringComparison.OrdinalIgnoreCase))
            {
                await HandleInvoiceMenuRequestAsync(chatId, text, ct);
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
                    ct: ct);
                return;
            }

            // پردازش گام‌های ورودی کاربر بر اساس استیت جاری
            await ProcessStepInputAsync(chatId, userState, text, contactPhone, fromUsername, ct);
        }

        private async Task HandleInvoiceMenuRequestAsync(string chatId, string actionTitle, CancellationToken ct)
        {
            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user == null || user.Profile == null || string.IsNullOrWhiteSpace(user.Profile.MemoryId))
            {
                await SendUserMessageAsync(chatId,
                    "⚠️ <b>حساب کاربری یا شناسه یکتای مالیاتی شما هنوز فعال نشده است.</b>\n\n" +
                    "جهت صدور یا ابطال صورتحساب، ابتدا باید از طریق دکمه «📝 ثبت‌نام و دریافت شناسه یکتا» اقدام نموده و یا در صورت داشتن حساب از دکمه «🔗 ورود / اتصال به حساب کاربری» استفاده نمایید.",
                    ct: ct);
                return;
            }

            if (actionTitle.Contains("ارسال"))
            {
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
                            new[] { new { text = "📥 دریافت نمونه فایل اکسل", callback_data = "inv_download_template" } }
                        }
                    },
                    ct: ct);
            }
            else
            {
                var recentInvoices = await _context.Invoices
                    .Where(i => i.Sender == user.Username && i.Status == InvoiceStatus.موفق && i.Ins == InvoiceTopic.اصلی)
                    .OrderByDescending(i => i.Id)
                    .Take(5)
                    .ToListAsync(ct);

                if (!recentInvoices.Any())
                {
                    await SendUserMessageAsync(chatId,
                        "ℹ️ در حال حاضر هیچ فاکتور ارسالی موفقی در سوابق شما جهت ابطال یافت نشد.\n\nدر صورتی که شماره مالیاتی ۲۲ رقمی فاکتور را دارید، می‌توانید آن را به صورت دستی ارسال نمایید.",
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

                await SendUserMessageAsync(chatId,
                    "❌ <b>ابطال فاکتور مالیاتی</b>\n\nلطفاً فاکتور مورد نظر جهت ابطال را از لیست زیر انتخاب کنید یا شناسه مالیاتی ۲۲ رقمی آن را در چت ارسال نمایید:",
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
                    "⚠️ شما هنوز حسابی در سامانه متصل نکرده‌اید. لطفاً ابتدا ثبت‌نام کرده یا وارد حساب خود شوید.", ct: ct);
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
                    "⚠️ شما هنوز حسابی در سامانه متصل نکرده‌اید. لطفاً ابتدا ثبت‌نام کرده یا وارد حساب خود شوید.", ct: ct);
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

            await SendUserMessageAsync(chatId, text, new { inline_keyboard = buttons.ToArray() }, ct: ct);
        }

        private async Task ShowProfileStatusAsync(string chatId, CancellationToken ct)
        {
            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.BaleChatId == chatId, ct);
            if (user != null)
            {
                var p = user.Profile;
                var memoryId = !string.IsNullOrWhiteSpace(p?.MemoryId) ? $"<code>{p.MemoryId}</code>" : "در انتظار صدور";
                var tin = p?.TinCode ?? "-";
                var fullName = p != null ? $"{p.FirstName} {p.Lastname}".Trim() : user.Username;

                var text = $"👤 <b>مشخصات حساب کاربری و پرونده مالیاتی</b>\n\n" +
                           $"• متقاضی/مودی: <b>{HtmlSafe(fullName)}</b>\n" +
                           $"• نام کاربری: <code>{user.Username}</code>\n" +
                           $"• کد اقتصادی / شناسه ملی: <code>{tin}</code>\n" +
                           $"• شناسه یکتای حافظه مالیاتی: {memoryId}\n" +
                           $"• موجودی کیف پول: <b>{user.Vallet:N0} ریال</b>\n\n" +
                           $"✅ وضعیت: <b>حساب متصل و آماده صدور صورتحساب</b>";

                await SendUserMessageAsync(chatId, text, ct: ct);
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

                object? markup = null;
                if (!request.IsPaid)
                {
                    markup = new
                    {
                        inline_keyboard = new[]
                        {
                            new[] { new { text = "💳 پرداخت هزینه و فعال‌سازی درخواست", callback_data = $"pay_reg:{request.Id}" } }
                        }
                    };
                }

                await SendUserMessageAsync(chatId, text, markup, ct: ct);
                return;
            }

            await SendUserMessageAsync(chatId,
                "ℹ️ در حال حاضر هیچ درخواست یا حسابی متصل به این حساب بله ثبت نشده است.\n\nجهت شروع می‌توانید از دکمه «📝 ثبت‌نام و دریافت شناسه یکتا» استفاده نمایید.",
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
                                new[] { new { text = "❌ انصراف و ثبت درخواست جدید", callback_data = $"cancel_reg:{existingReq.Id}" } }
                            }
                        },
                        ct: ct);
                    return;
                }

                await SendUserMessageAsync(chatId,
                    $"✅ درخواست شما قبلاً با کد پیگیری #{existingReq.Id} پرداخت و ثبت شده و در نوبت تیم کارشناسی قرار دارد. به محض صدور شناسه یکتا، به شما اطلاع‌رسانی خواهد شد.",
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
                    userState.CurrentStep = "None";
                    userState.StateDataJson = null;
                    await _context.SaveChangesAsync(ct);
                    await ShowMainMenuAsync(chatId, ct);
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

        public static string ConvertHtmlToMarkdown(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            var text = html;

            // تبدیل تگ‌های بولد با پشتیبانی از فاصله‌های درون تگ
            text = Regex.Replace(text, @"<\s*(b|strong)\s*>\s*(.*?)\s*<\s*/\s*(b|strong)\s*>", "*$2*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<\s*/?\s*(b|strong)\s*>", "*", RegexOptions.IgnoreCase);

            // تبدیل تگ‌های ایتالیک با پشتیبانی از فاصله‌های درون تگ
            text = Regex.Replace(text, @"<\s*(i|em)\s*>\s*(.*?)\s*<\s*/\s*(i|em)\s*>", "_$2_", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<\s*/?\s*(i|em)\s*>", "_", RegexOptions.IgnoreCase);

            // تبدیل تگ‌های کد درون‌خطی
            text = Regex.Replace(text, @"<\s*code\s*>\s*(.*?)\s*<\s*/\s*code\s*>", "`$2`", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<\s*/?\s*code\s*>", "`", RegexOptions.IgnoreCase);

            // تبدیل تگ‌های بلوک کد
            text = Regex.Replace(text, @"<\s*pre\s*>\s*(.*?)\s*<\s*/\s*pre\s*>", "```\n$1\n```", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<\s*/?\s*pre\s*>", "```", RegexOptions.IgnoreCase);

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
            var isRegistered = user != null && user.Profile != null;

            var welcome = isRegistered
                ? $"سلام <b>{HtmlSafe(user.DisplayName)}</b> عزیز، به پنل اختصاصی سامانه مودیان خوش آمدید.\nجهت انجام امور از منوی زیر استفاده فرمایید:"
                : "👋 سلام، به <b>ربات سامانه صدور فاکتور و کارپوشه مودیان</b> خوش آمدید.\n\n" +
                  "جهت دریافت شناسه یکتا و ارسال فاکتور، لطفاً از گزینه‌های زیر استفاده نمایید:";

            await SendUserMessageAsync(chatId, welcome, GetMainUserReplyKeyboard(isRegistered), ct: ct);
        }

        private static object GetMainUserReplyKeyboard(bool isRegistered)
        {
            if (isRegistered)
            {
                return new
                {
                    keyboard = new[]
                    {
                        new[] { new { text = "📄 ارسال فاکتور جدید" }, new { text = "❌ ابطال فاکتور" } },
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
    }
}
