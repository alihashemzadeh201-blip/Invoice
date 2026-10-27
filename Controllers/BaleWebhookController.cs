using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Code;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Invoice.Controllers
{
    [ApiController]
    [Route("api/bale")]
    [AllowAnonymous]
    public class BaleWebhookController : ControllerBase
    {
        private readonly IBaleUpdateProcessor _updateProcessor;
        private readonly IBaleSettingsService _settingsService;
        private readonly ILogger<BaleWebhookController> _logger;

        public BaleWebhookController(
            IBaleUpdateProcessor updateProcessor,
            IBaleSettingsService settingsService,
            ILogger<BaleWebhookController> logger)
        {
            _updateProcessor = updateProcessor;
            _settingsService = settingsService;
            _logger = logger;
        }

        [HttpGet("status")]
        [HttpGet("webhook")]
        public async Task<IActionResult> GetStatus()
        {
            var settings = await _settingsService.GetSettingsAsync();
            return Ok(new
            {
                service = "Bale Messenger Bot Webhook Endpoint",
                status = "running",
                enabled = settings.Enabled,
                interactivityEnabled = settings.BotInteractivityEnabled,
                updateMode = settings.UpdateMode,
                serverTime = DateTime.Now
            });
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> ReceiveUpdate([FromBody] JsonElement update, CancellationToken ct)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync();

                if (!settings.Enabled || !settings.BotInteractivityEnabled)
                {
                    return Ok();
                }

                // اعتبارسنجی توکن محرمانه وب‌هوک (در صورت تنظیم شدن در پنل)
                if (!string.IsNullOrWhiteSpace(settings.WebhookSecretToken))
                {
                    var tokenMatches = false;
                    if (Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var headerVal))
                    {
                        tokenMatches = string.Equals(headerVal, settings.WebhookSecretToken, StringComparison.Ordinal);
                    }

                    if (!tokenMatches && Request.Query.TryGetValue("token", out var queryVal))
                    {
                        tokenMatches = string.Equals(queryVal, settings.WebhookSecretToken, StringComparison.Ordinal);
                    }

                    if (!tokenMatches)
                    {
                        _logger.LogWarning("درخواست وب‌هوک بله با توکن امنیتی نامعتبر دریافت شد.");
                        return Unauthorized(new { error = "Invalid webhook secret token." });
                    }
                }

                await _updateProcessor.ProcessUpdateAsync(update, ct);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "استثنا در کنترلر وب‌هوک پیام‌رسان بله.");
                return Ok(); // بازگرداندن وضعیت ۲۰۰ جهت جلوگیری از تکرار بی‌پایان درخواست از سمت بله
            }
        }
    }
}