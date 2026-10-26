using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Pages.Admin.Bale;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class BaleAdminPageTests
    {
        private readonly Mock<IBaleSettingsService> _settingsServiceMock;
        private readonly Mock<IBaleNotificationService> _notificationServiceMock;
        private readonly Mock<ILogger<IndexModel>> _loggerMock;

        public BaleAdminPageTests()
        {
            _settingsServiceMock = new Mock<IBaleSettingsService>();
            _notificationServiceMock = new Mock<IBaleNotificationService>();
            _loggerMock = new Mock<ILogger<IndexModel>>();
        }

        [Fact]
        public async Task OnPostSaveAsync_WhenBotTokenIsEmpty_PreservesExistingToken()
        {
            // Arrange
            var existingSettings = new BaleSettings
            {
                BotToken = "original-bot-token-12345",
                ChatId = "chat-111",
                Enabled = true
            };

            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(existingSettings);

            BaleSettings? savedSettings = null;
            _settingsServiceMock.Setup(s => s.SaveSettingsAsync(It.IsAny<BaleSettings>()))
                .Callback<BaleSettings>(s => savedSettings = s)
                .Returns(Task.CompletedTask);

            var page = new IndexModel(_settingsServiceMock.Object, _notificationServiceMock.Object, _loggerMock.Object);
            // Simulating form submit where BotToken is empty/whitespace because password field was not re-entered
            page.Settings = new BaleSettings
            {
                BotToken = "   ",
                ChatId = "new-chat-999",
                Enabled = false,
                CustomTitle = "سامانه مالی جدید"
            };

            // Act
            var result = await page.OnPostSaveAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(savedSettings);
            // CRUCIAL: token must NOT be wiped out
            Assert.Equal("original-bot-token-12345", savedSettings.BotToken);
            Assert.Equal("new-chat-999", savedSettings.ChatId);
            Assert.False(savedSettings.Enabled);
            Assert.Equal("سامانه مالی جدید", savedSettings.CustomTitle);
        }

        [Fact]
        public async Task OnPostSaveAsync_WhenNewBotTokenIsProvided_UpdatesToken()
        {
            // Arrange
            var existingSettings = new BaleSettings
            {
                BotToken = "old-token",
                ChatId = "chat-111"
            };

            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(existingSettings);

            BaleSettings? savedSettings = null;
            _settingsServiceMock.Setup(s => s.SaveSettingsAsync(It.IsAny<BaleSettings>()))
                .Callback<BaleSettings>(s => savedSettings = s)
                .Returns(Task.CompletedTask);

            var page = new IndexModel(_settingsServiceMock.Object, _notificationServiceMock.Object, _loggerMock.Object);
            page.Settings = new BaleSettings
            {
                BotToken = " new-token-555 ",
                ChatId = "chat-222"
            };

            // Act
            var result = await page.OnPostSaveAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(savedSettings);
            Assert.Equal("new-token-555", savedSettings.BotToken);
        }

        [Fact]
        public async Task OnPostClearTokenAsync_ClearsTokenSuccessfully()
        {
            // Arrange
            var existingSettings = new BaleSettings
            {
                BotToken = "token-to-delete",
                ChatId = "chat-111"
            };

            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(existingSettings);

            BaleSettings? savedSettings = null;
            _settingsServiceMock.Setup(s => s.SaveSettingsAsync(It.IsAny<BaleSettings>()))
                .Callback<BaleSettings>(s => savedSettings = s)
                .Returns(Task.CompletedTask);

            var page = new IndexModel(_settingsServiceMock.Object, _notificationServiceMock.Object, _loggerMock.Object);

            // Act
            var result = await page.OnPostClearTokenAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(savedSettings);
            Assert.Equal(string.Empty, savedSettings.BotToken);
        }

        [Fact]
        public async Task OnPostSaveAsync_SavesAndTrimsBaseUrl()
        {
            // Arrange
            var existingSettings = new BaleSettings
            {
                BotToken = "some-token",
                ChatId = "chat-111",
                BaseUrl = "https://old.example.com"
            };

            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(existingSettings);

            BaleSettings? savedSettings = null;
            _settingsServiceMock.Setup(s => s.SaveSettingsAsync(It.IsAny<BaleSettings>()))
                .Callback<BaleSettings>(s => savedSettings = s)
                .Returns(Task.CompletedTask);

            var page = new IndexModel(_settingsServiceMock.Object, _notificationServiceMock.Object, _loggerMock.Object);
            page.Settings = new BaleSettings
            {
                BaseUrl = "  https://new.factor.com  ",
                ChatId = "chat-222"
            };

            // Act
            var result = await page.OnPostSaveAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(savedSettings);
            Assert.Equal("https://new.factor.com", savedSettings.BaseUrl);
        }
    }
}