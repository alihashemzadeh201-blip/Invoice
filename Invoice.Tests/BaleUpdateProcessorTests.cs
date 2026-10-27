using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Code;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class BaleUpdateProcessorTests
    {
        private readonly Mock<IBaleSettingsService> _settingsServiceMock;
        private readonly Mock<IBaleNotificationService> _notificationServiceMock;
        private readonly Mock<IBaleBotActionService> _actionServiceMock;
        private readonly Mock<ILogger<BaleUpdateProcessor>> _loggerMock;
        private readonly BaleUpdateProcessor _processor;

        public BaleUpdateProcessorTests()
        {
            _settingsServiceMock = new Mock<IBaleSettingsService>();
            _notificationServiceMock = new Mock<IBaleNotificationService>();
            _actionServiceMock = new Mock<IBaleBotActionService>();
            _loggerMock = new Mock<ILogger<BaleUpdateProcessor>>();

            _processor = new BaleUpdateProcessor(
                _settingsServiceMock.Object,
                _notificationServiceMock.Object,
                _actionServiceMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task ProcessUpdateAsync_WhenUnauthorizedUser_AnswersCallbackWithForbidden()
        {
            // Arrange
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    ChatId = "99999",
                    AdminChatIds = "88888"
                });

            var json = @"{
                ""update_id"": 1,
                ""callback_query"": {
                    ""id"": ""query_1"",
                    ""from"": { ""id"": 11111, ""username"": ""stranger"" },
                    ""message"": { ""message_id"": 55, ""chat"": { ""id"": 11111 }, ""text"": ""درخواست کیف پول"" },
                    ""data"": ""wallet:approve:10""
                }
            }";
            using var doc = JsonDocument.Parse(json);

            // Act
            await _processor.ProcessUpdateAsync(doc.RootElement);

            // Assert
            _actionServiceMock.Verify(a => a.HandleActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _notificationServiceMock.Verify(n => n.AnswerCallbackQueryAsync("query_1", It.Is<string>(s => s.Contains("دسترسی")), true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProcessUpdateAsync_WhenAuthorizedCallbackQuery_DispatchesActionAndEditsMessage()
        {
            // Arrange
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    ChatId = "12345",
                    AdminChatIds = ""
                });

            _actionServiceMock.Setup(a => a.HandleActionAsync("wallet", "approve", 15, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BaleActionResult.Ok("موفقیت‌آمیز", "تایید شد"));

            var json = @"{
                ""update_id"": 2,
                ""callback_query"": {
                    ""id"": ""query_2"",
                    ""from"": { ""id"": 12345, ""username"": ""admin"" },
                    ""message"": { ""message_id"": 66, ""chat"": { ""id"": 12345 }, ""text"": ""درخواست افزایش موجودی کیف پول"" },
                    ""data"": ""wallet:approve:15""
                }
            }";
            using var doc = JsonDocument.Parse(json);

            // Act
            await _processor.ProcessUpdateAsync(doc.RootElement);

            // Assert
            _actionServiceMock.Verify(a => a.HandleActionAsync("wallet", "approve", 15, It.Is<string>(s => s.Contains("admin")), It.IsAny<CancellationToken>()), Times.Once);
            _notificationServiceMock.Verify(n => n.AnswerCallbackQueryAsync("query_2", "تایید شد", false, It.IsAny<CancellationToken>()), Times.Once);
            _notificationServiceMock.Verify(n => n.EditMessageTextAsync("12345", 66, It.Is<string>(s => s.Contains("تایید شد") && s.Contains("وضعیت")), "HTML", null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProcessUpdateAsync_WhenAuthorizedMessageCommand_DispatchesAndSendsReply()
        {
            // Arrange
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    ChatId = "12345"
                });

            _actionServiceMock.Setup(a => a.ProcessTextCommandAsync("/status", "12345", "admin", It.IsAny<CancellationToken>()))
                .ReturnsAsync(BaleActionResult.Ok("سامانه فعال است"));

            var json = @"{
                ""update_id"": 3,
                ""message"": {
                    ""message_id"": 77,
                    ""from"": { ""id"": 12345, ""username"": ""admin"" },
                    ""chat"": { ""id"": 12345 },
                    ""text"": ""/status""
                }
            }";
            using var doc = JsonDocument.Parse(json);

            // Act
            await _processor.ProcessUpdateAsync(doc.RootElement);

            // Assert
            _actionServiceMock.Verify(a => a.ProcessTextCommandAsync("/status", "12345", "admin", It.IsAny<CancellationToken>()), Times.Once);
            _notificationServiceMock.Verify(n => n.SendMessageAsync("سامانه فعال است", "12345", "HTML", null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}