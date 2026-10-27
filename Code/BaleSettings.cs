using System;
using System.Collections.Generic;

namespace Invoice.Code
{
    public class BaleSettings
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
        public bool NotifyOnKarpooshehRequest { get; set; } = true;
        public bool NotifyOnWalletRequest { get; set; } = true;
        public bool NotifyOnCreditPlanRequest { get; set; } = true;
        public bool NotifyOnUserRegister { get; set; } = true;
        public string CustomTitle { get; set; } = "سامانه صدور فاکتور";

        // تنظیمات تایید و رد درخواست‌ها از طریق پیام‌رسان بله
        public bool BotInteractivityEnabled { get; set; } = true;
        public string UpdateMode { get; set; } = "Polling"; // "Polling" یا "Webhook"
        public string WebhookSecretToken { get; set; } = string.Empty;
        public string AdminChatIds { get; set; } = string.Empty;

        // تنظیمات اختصاصی ربات کاربر پیام‌رسان بله (User Bot)
        public bool UserBotEnabled { get; set; } = false;
        public string UserBotToken { get; set; } = string.Empty;
        public string UserBotPaymentProviderToken { get; set; } = string.Empty;
        public int RegistrationFee { get; set; } = 5000000; // ریال
        public string UserBotWebhookSecret { get; set; } = string.Empty;
    }

    public class BaleBotInfo
    {
        public bool Success { get; set; }
        public long Id { get; set; }
        public string? FirstName { get; set; }
        public string? Username { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class BaleChatSummary
    {
        public string ChatId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? LastMessageText { get; set; }
        public DateTime? LastMessageDate { get; set; }
    }
}
