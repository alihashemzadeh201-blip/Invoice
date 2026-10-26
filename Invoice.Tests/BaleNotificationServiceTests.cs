using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class BaleNotificationServiceTests
    {
        private class TestHttpMessageHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastRequestBody { get; private set; }
            public HttpResponseMessage ResponseToReturn { get; set; } = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":123}}")
            };

            public bool ShouldThrow { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                if (request.Content != null)
                {
                    LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                }

                if (ShouldThrow)
                {
                    throw new HttpRequestException("Simulated network connection failure");
                }

                return ResponseToReturn;
            }
        }

        private readonly Mock<IBaleSettingsService> _settingsServiceMock;
        private readonly Mock<ILogger<BaleNotificationService>> _loggerMock;
        private readonly TestHttpMessageHandler _httpHandler;
        private readonly HttpClient _httpClient;
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

        public BaleNotificationServiceTests()
        {
            _settingsServiceMock = new Mock<IBaleSettingsService>();
            _loggerMock = new Mock<ILogger<BaleNotificationService>>();
            _httpHandler = new TestHttpMessageHandler();
            _httpClient = new HttpClient(_httpHandler);

            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _httpClientFactoryMock.Setup(f => f.CreateClient("BaleClient")).Returns(_httpClient);
        }

        [Fact]
        public async Task SendKarpooshehRequestNotificationAsync_WhenDisabled_DoesNotSendHttp()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = false,
                    BotToken = "token123",
                    ChatId = "chat123",
                    NotifyOnKarpooshehRequest = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var req = new KarpooshehRequest
            {
                Id = 1,
                Name = "شرکت تست",
                NationalId = "10101010101",
                Phone = "09123456789",
                JobTitle = "مدیر عامل"
            };

            await service.SendKarpooshehRequestNotificationAsync(req);

            Assert.Null(_httpHandler.LastRequest);
        }
        [Fact]
        public async Task SendKarpooshehRequestNotificationAsync_WhenEventDisabled_DoesNotSendHttp()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token123",
                    ChatId = "chat123",
                    NotifyOnKarpooshehRequest = false
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var req = new KarpooshehRequest
            {
                Id = 2,
                Name = "شرکت نمونه",
                NationalId = "12345678901",
                Phone = "09120000000",
                JobTitle = "حسابدار"
            };

            await service.SendKarpooshehRequestNotificationAsync(req);

            Assert.Null(_httpHandler.LastRequest);
        }

        [Fact]
        public async Task SendKarpooshehRequestNotificationAsync_WhenEnabled_SendsMessageWithDetails()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token123",
                    ChatId = "chat123",
                    NotifyOnKarpooshehRequest = true,
                    CustomTitle = "سامانه تست"
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var req = new KarpooshehRequest
            {
                Id = 10,
                Name = "شرکت آلفا",
                NationalId = "10109988776",
                Phone = "09123456789",
                JobTitle = "مدیر عامل",
                PersonType = PersonType.حقوقی,
                Email = "info@alpha.com"
            };

            await service.SendKarpooshehRequestNotificationAsync(req);

            Assert.NotNull(_httpHandler.LastRequest);
            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("شرکت آلفا", text);
            Assert.Contains("10109988776", text);
            Assert.Contains("09123456789", text);
            Assert.Contains("مدیر عامل", text);
        }

        [Fact]
        public async Task SendWalletRequestNotificationAsync_WhenEnabled_SendsMessageWithDetails()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "testbot:token",
                    ChatId = "987654",
                    NotifyOnWalletRequest = true,
                    CustomTitle = "سامانه مودیان تست"
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var transaction = new Transaction
            {
                Id = 10,
                CustomerUsername = "user_ali",
                Price = 500000,
                Description = "واریز جهت شارژ فاکتور",
                BankTransactionReceipt = "FISH-9988",
                SourceCardNumber = "6037991122334455",
                Date = new DateTime(2025, 5, 1, 14, 30, 0)
            };

            await service.SendWalletRequestNotificationAsync(transaction);

            Assert.NotNull(_httpHandler.LastRequest);
            Assert.Equal("https://tapi.bale.ai/bottestbot:token/sendMessage", _httpHandler.LastRequest.RequestUri?.ToString());

            Assert.NotNull(_httpHandler.LastRequestBody);
            using var doc = JsonDocument.Parse(_httpHandler.LastRequestBody);
            var root = doc.RootElement;
            Assert.Equal("987654", root.GetProperty("chat_id").GetString());
            var text = root.GetProperty("text").GetString();
            Assert.NotNull(text);
            Assert.Contains("کیف پول", text);
            Assert.Contains("user_ali", text);
            Assert.Contains("500,000", text);
            Assert.Contains("FISH-9988", text);
        }

        [Fact]
        public async Task SendCreditPlanRequestNotificationAsync_WhenEnabled_SendsCorrectPayload()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "planbot:token",
                    ChatId = "55555",
                    NotifyOnCreditPlanRequest = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var creditRequest = new CreditRequest
            {
                Id = 5,
                Username = "merchant_reza",
                Months = 6,
                Price = 1200000,
                Date = DateTime.Now
            };

            await service.SendCreditPlanRequestNotificationAsync(creditRequest);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("خرید بسته", text);
            Assert.Contains("merchant_reza", text);
            Assert.Contains("6", text);
            Assert.Contains("1,200,000", text);
        }

        [Fact]
        public async Task SendUserRegisteredNotificationAsync_WhenEnabled_SendsCorrectPayload()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "regbot:token",
                    ChatId = "77777",
                    NotifyOnUserRegister = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var newUser = new User
            {
                Username = "new_customer_01",
                RegisterDate = DateTime.Now
            };

            await service.SendUserRegisteredNotificationAsync(newUser);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("ثبت‌نام کاربر جدید", text);
            Assert.Contains("new_customer_01", text);
        }

        [Fact]
        public async Task SendMessageAsync_WhenHttpThrows_DoesNotCrashAndReturnsFalse()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "failbot:token",
                    ChatId = "11111"
                });

            _httpHandler.ShouldThrow = true;

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);

            var result = await service.SendMessageAsync("پیام تستی");

            Assert.False(result);
        }

        [Fact]
        public async Task SendKarpooshehRequestNotificationAsync_WhenBaseUrlConfigured_IncludesDirectLinkToDetailsPage()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token:123",
                    ChatId = "12345",
                    BaseUrl = "https://factor.example.ir",
                    NotifyOnKarpooshehRequest = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var req = new KarpooshehRequest
            {
                Id = 42,
                Name = "شرکت تست",
                NationalId = "1010101010",
                Phone = "09120000000"
            };

            await service.SendKarpooshehRequestNotificationAsync(req);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("https://factor.example.ir/Admin/Requests/Details?id=42", text);
            Assert.Contains("مشاهده و بررسی درخواست کارپوشه", text);
        }

        [Fact]
        public async Task SendKarpooshehRequestNotificationAsync_WhenNoId_IncludesLinkToIndexPage()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token:123",
                    ChatId = "12345",
                    BaseUrl = "https://factor.example.ir",
                    NotifyOnKarpooshehRequest = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var req = new KarpooshehRequest
            {
                Id = 0,
                Name = "شرکت تست بدون شناسه",
                NationalId = "1010101010",
                Phone = "09120000000"
            };

            await service.SendKarpooshehRequestNotificationAsync(req);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("https://factor.example.ir/Admin/Requests/Index", text);
        }

        [Fact]
        public async Task SendWalletRequestNotificationAsync_WhenBaseUrlConfigured_IncludesDirectLinkToConfirmPage()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token:123",
                    ChatId = "12345",
                    BaseUrl = "https://factor.example.ir",
                    NotifyOnWalletRequest = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var tx = new Transaction
            {
                Id = 88,
                CustomerUsername = "wallet_user",
                Price = 500000
            };

            await service.SendWalletRequestNotificationAsync(tx);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("https://factor.example.ir/Admin/Transaction/Confirm?id=88", text);
            Assert.Contains("بررسی و تایید تراکنش کیف پول", text);
        }

        [Fact]
        public async Task SendCreditPlanRequestNotificationAsync_WhenBaseUrlConfigured_IncludesDirectLinkToSetExpire()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token:123",
                    ChatId = "12345",
                    BaseUrl = "https://factor.example.ir",
                    NotifyOnCreditPlanRequest = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var planReq = new CreditRequest
            {
                Id = 15,
                Username = "ali_user",
                Months = 3,
                Price = 900000
            };

            await service.SendCreditPlanRequestNotificationAsync(planReq);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("https://factor.example.ir/Admin/Users/SetExpire?username=ali_user", text);
            Assert.Contains("مشاهده و مدیریت اعتبار کاربر", text);
        }

        [Fact]
        public async Task SendUserRegisteredNotificationAsync_WhenBaseUrlConfigured_IncludesDirectLinkToAddUser()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token:123",
                    ChatId = "12345",
                    BaseUrl = "https://factor.example.ir",
                    NotifyOnUserRegister = true
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);
            var user = new User
            {
                Username = "reza_user",
                Email = "reza@test.com"
            };

            await service.SendUserRegisteredNotificationAsync(user);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("https://factor.example.ir/Admin/Users/AddUser?username=reza_user", text);
            Assert.Contains("مشاهده اطلاعات کاربر در پنل", text);
        }

        [Fact]
        public async Task SendTestNotificationAsync_WhenBaseUrlConfigured_IncludesDirectLinkToAdminBaleIndex()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token:123",
                    ChatId = "12345",
                    BaseUrl = "https://factor.example.ir"
                });

            var service = new BaleNotificationService(_httpClientFactoryMock.Object, _settingsServiceMock.Object, _loggerMock.Object);

            await service.SendTestNotificationAsync();

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("https://factor.example.ir/Admin/Bale/Index", text);
            Assert.Contains("مشاهده تنظیمات بله در پنل مدیریت", text);
        }

        [Fact]
        public async Task SendNotification_WhenBaseUrlEmpty_UsesHttpContextRequest()
        {
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    Enabled = true,
                    BotToken = "token:123",
                    ChatId = "12345",
                    BaseUrl = "",
                    NotifyOnKarpooshehRequest = true
                });

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("dynamic.myhost.com", 8443);

            var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            httpContextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);

            var service = new BaleNotificationService(
                _httpClientFactoryMock.Object,
                _settingsServiceMock.Object,
                _loggerMock.Object,
                httpContextAccessorMock.Object);

            var req = new KarpooshehRequest { Id = 77, Name = "تست داینامیک" };
            await service.SendKarpooshehRequestNotificationAsync(req);

            Assert.NotNull(_httpHandler.LastRequestBody);
            var text = JsonDocument.Parse(_httpHandler.LastRequestBody).RootElement.GetProperty("text").GetString();
            Assert.Contains("https://dynamic.myhost.com:8443/Admin/Requests/Details?id=77", text);
        }

    }
}
