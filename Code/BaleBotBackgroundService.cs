using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public class BaleBotBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBaleSettingsService _settingsService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BaleBotBackgroundService> _logger;
        private long _lastOffset = 0;

        public BaleBotBackgroundService(
            IServiceScopeFactory scopeFactory,
            IBaleSettingsService settingsService,
            IHttpClientFactory httpClientFactory,
            ILogger<BaleBotBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _settingsService = settingsService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("سرویس پس‌زمینه دریافت دستورات و پاسخ به دکمه‌های پیام‌رسان بله راه‌اندازی شد.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var settings = await _settingsService.GetSettingsAsync();

                    if (!settings.Enabled ||
                        !settings.BotInteractivityEnabled ||
                        string.IsNullOrWhiteSpace(settings.BotToken) ||
                        !string.Equals(settings.UpdateMode, "Polling", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        continue;
                    }

                    var token = settings.BotToken.Trim();
                    var client = _httpClientFactory.CreateClient("BaleClient");
                    var url = $"https://tapi.bale.ai/bot{token}/getUpdates";

                    var query = new
                    {
                        offset = _lastOffset > 0 ? (long?)_lastOffset : null,
                        limit = 25,
                        timeout = 10
                    };

                    using var content = new StringContent(
                        JsonSerializer.Serialize(query),
                        Encoding.UTF8,
                        "application/json");

                    using var response = await client.PostAsync(url, content, stoppingToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(stoppingToken);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                            root.TryGetProperty("result", out var updates) &&
                            updates.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var update in updates.EnumerateArray())
                            {
                                if (update.TryGetProperty("update_id", out var uidProp) && uidProp.TryGetInt64(out var uid))
                                {
                                    if (uid >= _lastOffset)
                                    {
                                        _lastOffset = uid + 1;
                                    }
                                }

                                try
                                {
                                    using var scope = _scopeFactory.CreateScope();
                                    var processor = scope.ServiceProvider.GetRequiredService<IBaleUpdateProcessor>();
                                    await processor.ProcessUpdateAsync(update.Clone(), stoppingToken);
                                }
                                catch (Exception exProc)
                                {
                                    _logger.LogError(exProc, "خطا در پردازش رویداد بله با شناسه {UpdateId}", _lastOffset - 1);
                                }
                            }
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        // تداخل با وب‌هوک فعال: اگر کاربر حالت Polling را انتخاب کرده ولی وب‌هوک بله هنوز ست است
                        _logger.LogWarning("درخواست getUpdates با خطای Conflict مواجه شد. وب‌هوک قبلی حذف می‌شود تا دریافت خودکار Polling ادامه یابد.");
                        await _settingsService.DeleteWebhookAsync(stoppingToken);
                        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    }
                    else
                    {
                        var errorBody = await response.Content.ReadAsStringAsync(stoppingToken);
                        _logger.LogWarning("خطا در getUpdates از بله: وضعیت {Status} - {Response}", response.StatusCode, errorBody);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "استثنا در سرویس پس‌زمینه بله. تلاش مجدد تا ۵ ثانیه دیگر...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("سرویس پس‌زمینه پیام‌رسان بله متوقف شد.");
        }
    }
}