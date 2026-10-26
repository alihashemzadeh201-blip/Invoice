using System;
using System.IO;
using System.Threading.Tasks;
using Invoice.Code;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class BaleSettingsServiceTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly Mock<IWebHostEnvironment> _envMock;
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly Mock<ILogger<BaleSettingsService>> _loggerMock;
        private readonly IConfiguration _configuration;

        public BaleSettingsServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "BaleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);

            _envMock = new Mock<IWebHostEnvironment>();
            _envMock.Setup(e => e.ContentRootPath).Returns(_tempDirectory);

            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _loggerMock = new Mock<ILogger<BaleSettingsService>>();

            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["Bale:BotToken"] = "default-token-from-config",
                ["Bale:ChatId"] = "12345678",
                ["Bale:Enabled"] = "true",
                ["Bale:CustomTitle"] = "سامانه تست"
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemoryConfig)
                .Build();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        [Fact]
        public async Task GetSettingsAsync_WhenFileDoesNotExist_ReturnsDefaultsFromConfig()
        {
            // Arrange
            var service = new BaleSettingsService(
                _envMock.Object,
                _configuration,
                _httpClientFactoryMock.Object,
                _loggerMock.Object);

            // Act
            var settings = await service.GetSettingsAsync();

            // Assert
            Assert.NotNull(settings);
            Assert.Equal("default-token-from-config", settings.BotToken);
            Assert.Equal("12345678", settings.ChatId);
            Assert.True(settings.Enabled);
            Assert.Equal("سامانه تست", settings.CustomTitle);
        }

        [Fact]
        public async Task SaveSettingsAsync_PersistsToFileAndCanBeReloaded()
        {
            // Arrange
            var service = new BaleSettingsService(
                _envMock.Object,
                _configuration,
                _httpClientFactoryMock.Object,
                _loggerMock.Object);

            var newSettings = new BaleSettings
            {
                BotToken = "new-saved-token:987654",
                ChatId = "998877",
                Enabled = true,
                CustomTitle = "سامانه مالی",
                NotifyOnKarpooshehRequest = true,
                NotifyOnWalletRequest = false,
                NotifyOnCreditPlanRequest = true,
                NotifyOnUserRegister = false
            };

            // Act
            await service.SaveSettingsAsync(newSettings);
            var reloaded = await service.GetSettingsAsync();

            // Assert
            Assert.NotNull(reloaded);
            Assert.Equal("new-saved-token:987654", reloaded.BotToken);
            Assert.Equal("998877", reloaded.ChatId);
            Assert.True(reloaded.Enabled);
            Assert.Equal("سامانه مالی", reloaded.CustomTitle);
            Assert.True(reloaded.NotifyOnKarpooshehRequest);
            Assert.False(reloaded.NotifyOnWalletRequest);
            Assert.True(reloaded.NotifyOnCreditPlanRequest);
            Assert.False(reloaded.NotifyOnUserRegister);

            var filePath = Path.Combine(_tempDirectory, "bale_settings.json");
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task TestBotTokenAsync_WithEmptyToken_ReturnsFailure()
        {
            // Arrange
            var service = new BaleSettingsService(
                _envMock.Object,
                _configuration,
                _httpClientFactoryMock.Object,
                _loggerMock.Object);

            // Act
            var result = await service.TestBotTokenAsync(string.Empty);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("خالی", result.ErrorMessage);
        }
    }
}
