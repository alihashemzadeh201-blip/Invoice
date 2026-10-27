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
    public class BaleUserBotBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBaleSettingsService _settingsService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BaleUserBotBackgroundService> _logger;
        private long _lastOffset = 0;

        public BaleUserBotBackgroundService(
            IServiceScopeFactory scopeFactory,
            IBaleSettingsService settingsService,
            IHttpClientFactory httpClientFactory,
            ILogger<BaleUserBotBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _settingsService = settingsService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("سرویس پس‌زمینه دریافت پیام‌های ربات کاربر بله راه‌اندازی شد.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var settings = await _settingsService.GetSettingsAsync();

                    if (!settings.UserBotEnabled ||
                        string.IsNullOrWhiteSpace(settings.UserBotToken))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        continue;
                    }

                    var token = settings.UserBotToken.Trim();
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
                                    var userBotService = scope.ServiceProvider.GetRequiredService<IBaleUserBotService>();
                                    await userBotService.ProcessUpdateAsync(update.Clone(), stoppingToken);
                                }
                                catch (Exception exProc)
                                {
                                    _logger.LogError(exProc, "خطا در پردازش پیام ربات کاربر بله با شناسه {UpdateId}", _lastOffset - 1);
                                }
                            }
                        }
                    }
                    else
                    {
                        var errorBody = await response.Content.ReadAsStringAsync(stoppingToken);
                        _logger.LogWarning("خطا در getUpdates ربات کاربر از بله: وضعیت {Status} - {Response}", response.StatusCode, errorBody);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "استثنا در سرویس پس‌زمینه ربات کاربر بله. تلاش مجدد تا ۵ ثانیه دیگر...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("سرویس پس‌زمینه ربات کاربر پیام‌رسان بله متوقف شد.");
        }
    }
}
