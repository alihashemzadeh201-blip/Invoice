using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Models;
using Invoices.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public class BaleActionResult
    {
        public bool Success { get; set; }
        public bool IsWarning { get; set; }
        public string Message { get; set; } = string.Empty;
        public string AlertMessage { get; set; } = string.Empty;

        public static BaleActionResult Ok(string message, string? alertMessage = null)
        {
            return new BaleActionResult
            {
                Success = true,
                IsWarning = false,
                Message = message,
                AlertMessage = alertMessage ?? message
            };
        }

        public static BaleActionResult Warning(string message, string? alertMessage = null)
        {
            return new BaleActionResult
            {
                Success = false,
                IsWarning = true,
                Message = message,
                AlertMessage = alertMessage ?? message
            };
        }

        public static BaleActionResult Fail(string message, string? alertMessage = null)
        {
            return new BaleActionResult
            {
                Success = false,
                IsWarning = false,
                Message = message,
                AlertMessage = alertMessage ?? message
            };
        }
    }

    public interface IBaleBotActionService
    {
        Task<BaleActionResult> HandleActionAsync(string requestType, string action, int id, string? operatorInfo = null, CancellationToken ct = default);
        Task<string> GetPendingSummaryAsync(CancellationToken ct = default);
        Task<BaleActionResult> ProcessTextCommandAsync(string commandText, string senderChatId, string? senderName = null, CancellationToken ct = default);
    }

    public class BaleBotActionService : IBaleBotActionService
    {
        private readonly DataContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILogger<BaleBotActionService> _logger;

        public BaleBotActionService(
            DataContext context,
            IUserActivityLogger activityLogger,
            ILogger<BaleBotActionService> logger)
        {
            _context = context;
            _activityLogger = activityLogger;
            _logger = logger;
        }

        public async Task<BaleActionResult> HandleActionAsync(string requestType, string action, int id, string? operatorInfo = null, CancellationToken ct = default)
        {
            if (id <= 0)
            {
                return BaleActionResult.Fail("شناسه درخواست نامعتبر است.");
            }

            var type = (requestType ?? string.Empty).Trim().ToLowerInvariant();
            var act = (action ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                switch (type)
                {
                    case "wallet":
                        return await HandleWalletActionAsync(act, id, operatorInfo, ct);

                    case "karpoosheh":
                        return await HandleKarpooshehActionAsync(act, id, operatorInfo, ct);

                    case "credit":
                        return await HandleCreditActionAsync(act, id, operatorInfo, ct);

                    default:
                        return BaleActionResult.Fail($"نوع درخواست '{requestType}' پشتیبانی نمی‌شود.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش عملیات {Action} برای درخواست {Type} #{Id}", action, requestType, id);
                return BaleActionResult.Fail($"خطا در انجام عملیات: {ex.Message}");
            }
        }

        private async Task<BaleActionResult> HandleWalletActionAsync(string action, int id, string? operatorInfo, CancellationToken ct)
        {
            var trans = await _context.Transactions
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (trans == null)
            {
                return BaleActionResult.Fail($"تراکنش افزایش کیف پول #{id} یافت نشد.");
            }

            if (action == "approve" || action == "confirm")
            {
                if (trans.Confirm == true)
                {
                    var confirmedDateStr = trans.ConfirmDate.HasValue
                        ? (MyDate.GetPersianDate(trans.ConfirmDate.Value)?.ToString() ?? trans.ConfirmDate.Value.ToString("yyyy/MM/dd"))
                        : "-";
                    return BaleActionResult.Warning($"تراکنش #{id} قبلاً در تاریخ {confirmedDateStr} تایید شده است.");
                }

                var customer = trans.Customer ?? await _context.Users.SingleOrDefaultAsync(p => p.Username == trans.CustomerUsername, ct);
                if (customer == null)
                {
                    return BaleActionResult.Fail($"کاربر '{trans.CustomerUsername}' در سامانه یافت نشد.");
                }

                var amount = trans.Price ?? 0;
                customer.Vallet += amount;
                trans.Confirm = true;
                trans.ConfirmDate = DateTime.Now;
                trans.CheckedByUsername = operatorInfo ?? "BaleBot";

                await _context.SaveChangesAsync(ct);

                await _activityLogger.LogAsync(
                    "کیف پول",
                    "تایید تراکنش از ربات بله",
                    $"تایید تراکنش #{id} و افزایش کیف پول کاربر {customer.Username} به مبلغ {amount:N0} ریال توسط {operatorInfo}",
                    entityId: id.ToString(),
                    ct: ct);

                return BaleActionResult.Ok(
                    $"✅ درخواست افزایش کیف پول کاربر <b>{customer.Username}</b> به مبلغ <b>{amount:N0} ریال</b> تایید و موجودی کاربر شارژ شد.",
                    $"✅ تایید شد. موجودی {customer.Username} شارژ گردید.");
            }
            else if (action == "reject" || action == "delete")
            {
                if (trans.Confirm == true)
                {
                    return BaleActionResult.Fail($"تراکنش #{id} قبلاً تایید شده است و امکان رد آن وجود ندارد.");
                }

                var username = trans.CustomerUsername ?? "-";
                var amount = trans.Price ?? 0;

                _context.Transactions.Remove(trans);
                await _context.SaveChangesAsync(ct);

                await _activityLogger.LogAsync(
                    "کیف پول",
                    "رد تراکنش از ربات بله",
                    $"رد و حذف درخواست تراکنش #{id} به مبلغ {amount:N0} ریال برای کاربر {username} توسط {operatorInfo}",
                    entityId: id.ToString(),
                    ct: ct);

                return BaleActionResult.Ok(
                    $"❌ درخواست افزایش کیف پول #{id} برای کاربر <b>{username}</b> به مبلغ <b>{amount:N0} ریال</b> رد شد.",
                    "❌ درخواست رد شد.");
            }

            return BaleActionResult.Fail($"عملیات '{action}' برای تراکنش کیف پول نامعتبر است.");
        }

        private async Task<BaleActionResult> HandleKarpooshehActionAsync(string action, int id, string? operatorInfo, CancellationToken ct)
        {
            var req = await _context.KarpooshehRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (req == null)
            {
                return BaleActionResult.Fail($"درخواست کارپوشه #{id} یافت نشد.");
            }

            if (action == "approve" || action == "receive")
            {
                if (req.ReceiveDate != null)
                {
                    var recDateStr = MyDate.GetPersianDate(req.ReceiveDate.Value)?.ToString() ?? "-";
                    return BaleActionResult.Warning($"درخواست کارپوشه متقاضی '{req.Name}' قبلاً در تاریخ {recDateStr} تایید و دریافت شده است.");
                }

                req.ReceiveDate = DateTime.Now;
                await _context.SaveChangesAsync(ct);

                await _activityLogger.LogAsync(
                    "کارپوشه",
                    "تایید درخواست کارپوشه از ربات بله",
                    $"ثبت دریافت درخواست شناسه یکتا برای متقاضی {req.Name} (شناسه ملی {req.NationalId}) توسط {operatorInfo}",
                    entityId: id.ToString(),
                    ct: ct);

                return BaleActionResult.Ok(
                    $"✅ درخواست کارپوشه متقاضی <b>{req.Name}</b> (کد ملی: <code>{req.NationalId}</code>) تایید و وضعیت آن به 'دریافت شد' تغییر یافت.",
                    $"✅ تایید شد. وضعیت کارپوشه {req.Name} ثبت دریافت گردید.");
            }
            else if (action == "reject" || action == "cancel" || action == "delete")
            {
                req.CancelDate = DateTime.Now;
                req.FinishDate = DateTime.Now;
                await _context.SaveChangesAsync(ct);

                await _activityLogger.LogAsync(
                    "کارپوشه",
                    "رد درخواست کارپوشه از ربات بله",
                    $"رد درخواست شناسه یکتا برای متقاضی {req.Name} (شناسه ملی {req.NationalId}) توسط {operatorInfo}",
                    entityId: id.ToString(),
                    ct: ct);

                return BaleActionResult.Ok(
                    $"❌ درخواست کارپوشه متقاضی <b>{req.Name}</b> رد و مختومه شد.",
                    "❌ درخواست کارپوشه رد شد.");
            }

            return BaleActionResult.Fail($"عملیات '{action}' برای درخواست کارپوشه نامعتبر است.");
        }

        private async Task<BaleActionResult> HandleCreditActionAsync(string action, int id, string? operatorInfo, CancellationToken ct)
        {
            var credit = await _context.CreditRequests
                .Include(c => c.Period)
                .FirstOrDefaultAsync(c => c.Id == id, ct);

            if (credit == null)
            {
                return BaleActionResult.Fail($"درخواست بسته اعتباری #{id} یافت نشد.");
            }

            if (action == "approve" || action == "confirm")
            {
                var user = await _context.Users.SingleOrDefaultAsync(u => u.Username == credit.Username, ct);
                if (user == null)
                {
                    return BaleActionResult.Fail($"کاربر '{credit.Username}' در سامانه یافت نشد.");
                }

                var baseDate = (user.ExpireDate.HasValue && user.ExpireDate.Value > DateTime.Now)
                    ? user.ExpireDate.Value
                    : DateTime.Now;
                var months = credit.Months ?? credit.Period?.Months ?? 1;

                user.ExpireDate = baseDate.AddMonths(months);
                user.LastChargeDate = DateTime.Now;

                _context.CreditRequests.Remove(credit);
                await _context.SaveChangesAsync(ct);

                var newExpireShamsi = MyDate.GetPersianDate(user.ExpireDate.Value)?.ToString() ?? "-";

                await _activityLogger.LogAsync(
                    "اعتبار",
                    "تایید بسته اعتباری از ربات بله",
                    $"تایید بسته {months} ماهه برای کاربر {user.Username} تا تاریخ {newExpireShamsi} توسط {operatorInfo}",
                    entityId: id.ToString(),
                    ct: ct);

                return BaleActionResult.Ok(
                    $"✅ بسته اعتباری کاربر <b>{user.Username}</b> به مدت <b>{months} ماه</b> تایید و فعال شد.\n📅 تاریخ انقضای جدید: <b>{newExpireShamsi}</b>",
                    $"✅ تایید شد. اعتبار کاربر {user.Username} به مدت {months} ماه تمدید گردید.");
            }
            else if (action == "reject" || action == "delete")
            {
                var username = credit.Username;
                _context.CreditRequests.Remove(credit);
                await _context.SaveChangesAsync(ct);

                await _activityLogger.LogAsync(
                    "اعتبار",
                    "رد بسته اعتباری از ربات بله",
                    $"رد درخواست بسته اعتباری #{id} کاربر {username} توسط {operatorInfo}",
                    entityId: id.ToString(),
                    ct: ct);

                return BaleActionResult.Ok(
                    $"❌ درخواست بسته اعتباری #{id} کاربر <b>{username}</b> رد شد.",
                    "❌ درخواست رد شد.");
            }

            return BaleActionResult.Fail($"عملیات '{action}' برای بسته اعتباری نامعتبر است.");
        }

        public async Task<string> GetPendingSummaryAsync(CancellationToken ct = default)
        {
            var pendingWallets = await _context.Transactions
                .CountAsync(t => t.Confirm == null || t.Confirm == false, ct);

            var pendingKarpoosheh = await _context.KarpooshehRequests
                .CountAsync(k => k.FinishDate == null && k.ReceiveDate == null, ct);

            var pendingCredits = await _context.CreditRequests.CountAsync(ct);

            var nowShamsi = MyDate.GetPersianDate(DateTime.Now)?.ToString() ?? DateTime.Now.ToString("yyyy/MM/dd HH:mm");

            return $@"📊 <b>گزارش درخواست‌های در انتظار سامانه</b>

▫️ 💰 <b>درخواست‌های شارژ کیف پول:</b> {pendingWallets} مورد
▫️ 📋 <b>درخواست‌های کارپوشه (شناسه یکتا):</b> {pendingKarpoosheh} مورد
▫️ 📦 <b>درخواست‌های خرید بسته اعتباری:</b> {pendingCredits} مورد

📅 <b>زمان گزارش:</b> {nowShamsi}";
        }

        public async Task<BaleActionResult> ProcessTextCommandAsync(string commandText, string senderChatId, string? senderName = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return BaleActionResult.Fail("دستور خالی است.");
            }

            var trimmed = commandText.Trim();
            var operatorInfo = !string.IsNullOrWhiteSpace(senderName)
                ? $"بله (@{senderName})"
                : $"بله ({senderChatId})";

            // 1) دستورات راهنما
            if (trimmed.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("راهنما", StringComparison.OrdinalIgnoreCase))
            {
                var helpMessage = @"🤖 <b>ربات مدیریت سامانه صدور فاکتور</b>

شما می‌توانید درخواست‌های سامانه را مستقیماً از طریق دکمه‌های شیشه‌ای زیر هر پیام یا با ارسال دستورات متنی زیر تایید یا رد کنید:

📋 <b>دستورات اصلی:</b>
▫️ <code>/pending</code> یا <code>درخواست‌ها</code> - آمار و خلاصه درخواست‌های در انتظار
▫️ <code>/status</code> یا <code>وضعیت</code> - بررسی وضعیت سامانه و ارتباط با ربات
▫️ <code>/help</code> یا <code>راهنما</code> - نمایش این راهنما

⚡ <b>دستورات تایید و رد متنی:</b>
▫️ <code>تایید کیف پول [شناسه]</code> - مثال: <code>تایید کیف پول 12</code>
▫️ <code>رد کیف پول [شناسه]</code> - مثال: <code>رد کیف پول 12</code>
▫️ <code>تایید کارپوشه [شناسه]</code> - مثال: <code>تایید کارپوشه 5</code>
▫️ <code>رد کارپوشه [شناسه]</code> - مثال: <code>رد کارپوشه 5</code>
▫️ <code>تایید بسته [شناسه]</code> - مثال: <code>تایید بسته 8</code>
▫️ <code>رد بسته [شناسه]</code> - مثال: <code>رد بسته 8</code>";

                return BaleActionResult.Ok(helpMessage);
            }

            // 2) دستور وضعیت
            if (trimmed.Equals("/status", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("وضعیت", StringComparison.OrdinalIgnoreCase))
            {
                var summary = await GetPendingSummaryAsync(ct);
                var statusMsg = $"🟢 <b>سامانه صدور فاکتور فعال است</b>\n\n{summary}";
                return BaleActionResult.Ok(statusMsg);
            }

            // 3) دستور درخواست‌های در انتظار
            if (trimmed.Equals("/pending", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("درخواست‌ها", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("درخواستها", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("درخواست ها", StringComparison.OrdinalIgnoreCase))
            {
                var summary = await GetPendingSummaryAsync(ct);
                return BaleActionResult.Ok(summary);
            }

            // 4) تحلیل دستورات تایید / رد متنی به زبان فارسی
            var matchFa = Regex.Match(
                trimmed,
                @"^(تایید|تأیید|قبول|رد|حذف)\s+(کیف\s*پول|کیف|تراکنش|کارپوشه|شناسه\s*یکتا|بسته|اعتبار)\s*#?(\d+)$",
                RegexOptions.IgnoreCase);

            if (matchFa.Success)
            {
                var verb = matchFa.Groups[1].Value;
                var target = matchFa.Groups[2].Value.Replace(" ", "");
                var id = int.Parse(matchFa.Groups[3].Value);

                var isApprove = verb.Contains("تایید") || verb.Contains("تأیید") || verb.Contains("قبول");
                var action = isApprove ? "approve" : "reject";

                string requestType;
                if (target.Contains("کیف") || target.Contains("تراکنش"))
                {
                    requestType = "wallet";
                }
                else if (target.Contains("کارپوشه") || target.Contains("شناسه"))
                {
                    requestType = "karpoosheh";
                }
                else
                {
                    requestType = "credit";
                }

                return await HandleActionAsync(requestType, action, id, operatorInfo, ct);
            }

            // 5) تحلیل دستورات انگلیسی مانند /approve_wallet_12 یا /reject_karpoosheh_5
            var matchEn = Regex.Match(
                trimmed,
                @"^\/?(approve|reject|confirm|delete)[_\s]+(wallet|karpoosheh|credit)[_\s]+#?(\d+)$",
                RegexOptions.IgnoreCase);

            if (matchEn.Success)
            {
                var action = matchEn.Groups[1].Value.ToLowerInvariant();
                if (action == "confirm") action = "approve";
                if (action == "delete") action = "reject";

                var requestType = matchEn.Groups[2].Value.ToLowerInvariant();
                var id = int.Parse(matchEn.Groups[3].Value);

                return await HandleActionAsync(requestType, action, id, operatorInfo, ct);
            }

            return BaleActionResult.Fail(
                "دستور ارسالی شناخته نشد. جهت مشاهده راهنما، عبارت <code>/help</code> یا <code>راهنما</code> را ارسال کنید.");
        }
    }
}