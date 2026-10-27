using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public interface IBaleUpdateProcessor
    {
        Task ProcessUpdateAsync(JsonElement update, CancellationToken ct = default);
    }

    public class BaleUpdateProcessor : IBaleUpdateProcessor
    {
        private readonly IBaleSettingsService _settingsService;
        private readonly IBaleNotificationService _notificationService;
        private readonly IBaleBotActionService _actionService;
        private readonly ILogger<BaleUpdateProcessor> _logger;

        public BaleUpdateProcessor(
            IBaleSettingsService settingsService,
            IBaleNotificationService notificationService,
            IBaleBotActionService actionService,
            ILogger<BaleUpdateProcessor> logger)
        {
            _settingsService = settingsService;
            _notificationService = notificationService;
            _actionService = actionService;
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

                if (update.TryGetProperty("channel_post", out var cp))
                {
                    await HandleMessageAsync(cp, ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش آپدیت دریافتی از پیام‌رسان بله.");
            }
        }

        private async Task HandleCallbackQueryAsync(JsonElement cb, CancellationToken ct)
        {
            var queryId = cb.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var data = cb.TryGetProperty("data", out var dProp) ? dProp.GetString() : null;

            long? fromId = null;
            string? fromUsername = null;
            string? fromName = null;
            if (cb.TryGetProperty("from", out var fromProp))
            {
                if (fromProp.TryGetProperty("id", out var fId)) fromId = fId.GetInt64();
                if (fromProp.TryGetProperty("username", out var fUn)) fromUsername = fUn.GetString();
                var fn = fromProp.TryGetProperty("first_name", out var fnProp) ? fnProp.GetString() : "";
                var ln = fromProp.TryGetProperty("last_name", out var lnProp) ? lnProp.GetString() : "";
                fromName = $"{fn} {ln}".Trim();
            }

            long? messageId = null;
            string? chatId = null;
            string? originalText = null;
            if (cb.TryGetProperty("message", out var msgProp))
            {
                if (msgProp.TryGetProperty("message_id", out var mId)) messageId = mId.GetInt64();
                if (msgProp.TryGetProperty("text", out var tProp)) originalText = tProp.GetString();
                if (msgProp.TryGetProperty("chat", out var chatProp) && chatProp.TryGetProperty("id", out var cId))
                {
                    chatId = cId.ToString();
                }
            }

            var isAuthorized = await IsAuthorizedAsync(fromId, chatId);
            if (!isAuthorized)
            {
                _logger.LogWarning("درخواست کلیک دکمه از کاربر غیرمجاز (چت: {ChatId}، کاربر: {UserId}) رد شد.", chatId, fromId);
                if (!string.IsNullOrEmpty(queryId))
                {
                    await _notificationService.AnswerCallbackQueryAsync(queryId, "⛔ شما دسترسی لازم برای انجام این عملیات را ندارید.", showAlert: true, ct);
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(data))
            {
                if (!string.IsNullOrEmpty(queryId))
                {
                    await _notificationService.AnswerCallbackQueryAsync(queryId, "دستور نامعتبر است.", false, ct);
                }
                return;
            }

            var parts = data.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[2], out var entityId))
            {
                if (!string.IsNullOrEmpty(queryId))
                {
                    await _notificationService.AnswerCallbackQueryAsync(queryId, "فرمت دستور دکمه نامعتبر است.", false, ct);
                }
                return;
            }

            var requestType = parts[0];
            var action = parts[1];
            var operatorDisplay = !string.IsNullOrWhiteSpace(fromUsername)
                ? $"@{fromUsername}"
                : (!string.IsNullOrWhiteSpace(fromName) ? fromName : fromId?.ToString() ?? "مدیر");
            var operatorInfo = $"بله ({operatorDisplay})";

            var result = await _actionService.HandleActionAsync(requestType, action, entityId, operatorInfo, ct);

            if (!string.IsNullOrEmpty(queryId))
            {
                await _notificationService.AnswerCallbackQueryAsync(
                    queryId,
                    result.AlertMessage,
                    showAlert: result.IsWarning || !result.Success,
                    ct: ct);
            }

            if (!string.IsNullOrEmpty(chatId) && messageId.HasValue && !string.IsNullOrEmpty(originalText))
            {
                var icon = result.Success ? (action == "approve" || action == "confirm" || action == "receive" ? "✅" : "❌") : "⚠️";
                var statusTitle = action == "approve" || action == "confirm" || action == "receive" ? "تایید شد" : "رد شد";
                var nowShamsi = MyDate.GetPersianDate(DateTime.Now)?.ToString() ?? "-";

                var resolutionNotice = $"\n\n{icon} <b>وضعیت:</b> {statusTitle} توسط {HtmlSafe(operatorDisplay)} در {nowShamsi}";
                var updatedText = originalText + resolutionNotice;

                await _notificationService.EditMessageTextAsync(
                    chatId,
                    messageId.Value,
                    updatedText,
                    "HTML",
                    replyMarkup: null,
                    ct: ct);
            }
        }

        private async Task HandleMessageAsync(JsonElement msg, CancellationToken ct)
        {
            var text = msg.TryGetProperty("text", out var tProp) ? tProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(text)) return;

            long? fromId = null;
            string? fromUsername = null;
            string? fromName = null;
            if (msg.TryGetProperty("from", out var fromProp))
            {
                if (fromProp.TryGetProperty("id", out var fId)) fromId = fId.GetInt64();
                if (fromProp.TryGetProperty("username", out var fUn)) fromUsername = fUn.GetString();
                var fn = fromProp.TryGetProperty("first_name", out var fnProp) ? fnProp.GetString() : "";
                var ln = fromProp.TryGetProperty("last_name", out var lnProp) ? lnProp.GetString() : "";
                fromName = $"{fn} {ln}".Trim();
            }

            string? chatId = null;
            if (msg.TryGetProperty("chat", out var chatProp) && chatProp.TryGetProperty("id", out var cId))
            {
                chatId = cId.ToString();
            }

            if (string.IsNullOrWhiteSpace(chatId)) return;

            var isAuthorized = await IsAuthorizedAsync(fromId, chatId);
            if (!isAuthorized)
            {
                _logger.LogWarning("پیام متنی از کاربر غیرمجاز (چت: {ChatId}، کاربر: {UserId}) نادیده گرفته شد.", chatId, fromId);
                await _notificationService.SendMessageAsync(
                    "⛔ شما دسترسی لازم به دستورات مدیریتی این ربات را ندارید.",
                    chatId,
                    "HTML",
                    null,
                    ct);
                return;
            }

            var senderDisplayName = !string.IsNullOrWhiteSpace(fromUsername)
                ? fromUsername
                : (!string.IsNullOrWhiteSpace(fromName) ? fromName : fromId?.ToString());

            var result = await _actionService.ProcessTextCommandAsync(text, chatId, senderDisplayName, ct);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                await _notificationService.SendMessageAsync(result.Message, chatId, "HTML", null, ct);
            }
        }

        private async Task<bool> IsAuthorizedAsync(long? fromId, string? chatId)
        {
            var settings = await _settingsService.GetSettingsAsync();
            if (!settings.Enabled) return false;

            var authorizedIds = (settings.AdminChatIds ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            if (!string.IsNullOrWhiteSpace(settings.ChatId))
            {
                authorizedIds.Add(settings.ChatId.Trim());
            }

            if (authorizedIds.Count == 0) return false;

            if (fromId.HasValue && authorizedIds.Contains(fromId.Value.ToString()))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(chatId) && authorizedIds.Contains(chatId.Trim()))
            {
                return true;
            }

            return false;
        }
    }
}