using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public interface IBaleSettingsService
    {
        Task<BaleSettings> GetSettingsAsync();
        Task SaveSettingsAsync(BaleSettings settings);
        Task<BaleBotInfo> TestBotTokenAsync(string? token = null, CancellationToken ct = default);
        Task<List<BaleChatSummary>> GetRecentChatsAsync(string? token = null, CancellationToken ct = default);
    }

    public class BaleSettingsService : IBaleSettingsService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BaleSettingsService> _logger;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private BaleSettings? _cachedSettings;

        private string SettingsFilePath => Path.Combine(_env.ContentRootPath, "bale_settings.json");

        public BaleSettingsService(
            IWebHostEnvironment env,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<BaleSettingsService> logger)
        {
            _env = env;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<BaleSettings> GetSettingsAsync()
        {
            if (_cachedSettings != null) return _cachedSettings;

            await _fileLock.WaitAsync();
            try
            {
                if (_cachedSettings != null) return _cachedSettings;

                if (File.Exists(SettingsFilePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(SettingsFilePath);
                        var loaded = JsonSerializer.Deserialize<BaleSettings>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (loaded != null)
                        {
                            _cachedSettings = loaded;
                            return _cachedSettings;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "خطا در خواندن فایل تنظیمات بله (bale_settings.json). استفاده از تنظیمات appsettings.");
                    }
                }

                var fallback = new BaleSettings
                {
                    BotToken = _configuration["Bale:BotToken"] ?? string.Empty,
                    ChatId = _configuration["Bale:ChatId"] ?? string.Empty,
                    BaseUrl = _configuration["Bale:BaseUrl"] ?? string.Empty,
                    Enabled = bool.TryParse(_configuration["Bale:Enabled"], out var en) && en,
                    NotifyOnKarpooshehRequest = !bool.TryParse(_configuration["Bale:NotifyOnKarpooshehRequest"], out var nk) || nk,
                    NotifyOnWalletRequest = !bool.TryParse(_configuration["Bale:NotifyOnWalletRequest"], out var nw) || nw,
                    NotifyOnCreditPlanRequest = !bool.TryParse(_configuration["Bale:NotifyOnCreditPlanRequest"], out var nc) || nc,
                    NotifyOnUserRegister = !bool.TryParse(_configuration["Bale:NotifyOnUserRegister"], out var nu) || nu,
                    CustomTitle = _configuration["Bale:CustomTitle"] ?? "سامانه صدور فاکتور"
                };

                _cachedSettings = fallback;
                return fallback;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveSettingsAsync(BaleSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            await _fileLock.WaitAsync();
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(SettingsFilePath, json);
                _cachedSettings = settings;
                _logger.LogInformation("تنظیمات پیام‌رسان بله با موفقیت ذخیره شد.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ذخیره‌سازی تنظیمات پیام‌رسان بله.");
                throw;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<BaleBotInfo> TestBotTokenAsync(string? token = null, CancellationToken ct = default)
        {
            var effectiveToken = token;
            if (effectiveToken == null)
            {
                var settings = await GetSettingsAsync();
                effectiveToken = settings.BotToken;
            }

            if (string.IsNullOrWhiteSpace(effectiveToken))
            {
                return new BaleBotInfo
                {
                    Success = false,
                    ErrorMessage = "توکن ربات وارد نشده است یا خالی است."
                };
            }

            try
            {
                var client = _httpClientFactory.CreateClient("BaleClient");
                var url = $"https://tapi.bale.ai/bot{effectiveToken.Trim()}/getMe";
                var response = await client.GetAsync(url, ct);

                var content = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var isOk = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
                if (isOk && root.TryGetProperty("result", out var resultProp))
                {
                    var id = resultProp.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0;
                    var firstName = resultProp.TryGetProperty("first_name", out var fnProp) ? fnProp.GetString() : null;
                    var username = resultProp.TryGetProperty("username", out var unProp) ? unProp.GetString() : null;

                    return new BaleBotInfo
                    {
                        Success = true,
                        Id = id,
                        FirstName = firstName,
                        Username = username
                    };
                }
                else
                {
                    var desc = root.TryGetProperty("description", out var dProp) ? dProp.GetString() : "پاسخ نامعتبر از سرور بله";
                    return new BaleBotInfo
                    {
                        Success = false,
                        ErrorMessage = desc
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در برقراری ارتباط با وب‌سرویس بله در getMe");
                return new BaleBotInfo
                {
                    Success = false,
                    ErrorMessage = $"خطا در برقراری ارتباط: {ex.Message}"
                };
            }
        }

        public async Task<List<BaleChatSummary>> GetRecentChatsAsync(string? token = null, CancellationToken ct = default)
        {
            var results = new List<BaleChatSummary>();
            var effectiveToken = token;
            if (effectiveToken == null)
            {
                var settings = await GetSettingsAsync();
                effectiveToken = settings.BotToken;
            }

            if (string.IsNullOrWhiteSpace(effectiveToken))
            {
                return results;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("BaleClient");
                var url = $"https://tapi.bale.ai/bot{effectiveToken.Trim()}/getUpdates";
                var response = await client.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("درخواست getUpdates به بله با وضعیت {Status} پاسخ داده شد.", response.StatusCode);
                    return results;
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
                {
                    return results;
                }

                if (!root.TryGetProperty("result", out var updatesProp) || updatesProp.ValueKind != JsonValueKind.Array)
                {
                    return results;
                }

                var chatMap = new Dictionary<string, BaleChatSummary>();

                foreach (var update in updatesProp.EnumerateArray())
                {
                    JsonElement messageElement = default;
                    if (update.TryGetProperty("message", out var m)) messageElement = m;
                    else if (update.TryGetProperty("channel_post", out var cp)) messageElement = cp;
                    else continue;

                    if (!messageElement.TryGetProperty("chat", out var chatElement)) continue;

                    var chatId = chatElement.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                    if (string.IsNullOrWhiteSpace(chatId)) continue;

                    var chatType = chatElement.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";
                    var title = chatElement.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                    var firstName = chatElement.TryGetProperty("first_name", out var fnProp) ? fnProp.GetString() : null;
                    var lastName = chatElement.TryGetProperty("last_name", out var lnProp) ? lnProp.GetString() : null;
                    var username = chatElement.TryGetProperty("username", out var unProp) ? unProp.GetString() : null;

                    var displayName = !string.IsNullOrWhiteSpace(title)
                        ? title
                        : $"{firstName} {lastName}".Trim();
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = username ?? chatId;
                    }

                    var text = messageElement.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
                    DateTime? date = null;
                    if (messageElement.TryGetProperty("date", out var dateProp) && dateProp.TryGetInt64(out var unixSeconds))
                    {
                        date = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
                    }

                    chatMap[chatId] = new BaleChatSummary
                    {
                        ChatId = chatId,
                        Title = displayName,
                        Type = chatType,
                        Username = username,
                        LastMessageText = text,
                        LastMessageDate = date
                    };
                }

                results = chatMap.Values.OrderByDescending(c => c.LastMessageDate ?? DateTime.MinValue).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در دریافت لیست چت‌ها از وب‌سرویس بله در getUpdates");
            }

            return results;
        }
    }
}
