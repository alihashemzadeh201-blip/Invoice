using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoice.Pages.StuffPages;
using Invoices.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class KarpooshehRegistrationAndStuffTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DataContext _context;
        private readonly Mock<IProfileSecretProtector> _secretProtectorMock;
        private readonly Mock<ILogger<KarpooshehRegistrationService>> _regLoggerMock;
        private readonly Mock<IUserActivityLogger> _activityLoggerMock;

        public KarpooshehRegistrationAndStuffTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<DataContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new DataContext(options);
            _context.Database.EnsureCreated();

            _secretProtectorMock = new Mock<IProfileSecretProtector>();
            _secretProtectorMock.Setup(p => p.Protect(It.IsAny<string>()))
                .Returns<string>(plain => $"ENC:{plain}");
            _secretProtectorMock.Setup(p => p.Unprotect(It.IsAny<string>()))
                .Returns<string>(cipher => cipher != null && cipher.StartsWith("ENC:") ? cipher.Substring(4) : cipher ?? string.Empty);

            _regLoggerMock = new Mock<ILogger<KarpooshehRegistrationService>>();
            _activityLoggerMock = new Mock<IUserActivityLogger>();
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        [Fact]
        public void RsaKeyHelper_GenerateKeyPair_ProducesValidRsa2048Keys()
        {
            // Act
            var keyPair = RsaKeyHelper.GenerateKeyPair(2048);

            // Assert
            Assert.NotNull(keyPair);
            Assert.False(string.IsNullOrWhiteSpace(keyPair.PrivateKeyPem));
            Assert.False(string.IsNullOrWhiteSpace(keyPair.PublicKeyPem));

            Assert.StartsWith("-----BEGIN PRIVATE KEY-----", keyPair.PrivateKeyPem.Trim());
            Assert.EndsWith("-----END PRIVATE KEY-----", keyPair.PrivateKeyPem.Trim());

            Assert.StartsWith("-----BEGIN PUBLIC KEY-----", keyPair.PublicKeyPem.Trim());
            Assert.EndsWith("-----END PUBLIC KEY-----", keyPair.PublicKeyPem.Trim());

            // بررسی قابلیت لود شدن کلید خصوصی با کلاس RSA در دات نت
            using var rsaPriv = RSA.Create();
            rsaPriv.ImportFromPem(keyPair.PrivateKeyPem);
            Assert.Equal(2048, rsaPriv.KeySize);

            // بررسی قابلیت لود شدن کلید عمومی
            using var rsaPub = RSA.Create();
            rsaPub.ImportFromPem(keyPair.PublicKeyPem);
            Assert.Equal(2048, rsaPub.KeySize);
        }

        [Fact]
        public async Task KarpooshehRegistrationService_CreatesUserWithDefaultPasswordAndGeneratesRsaKeys()
        {
            // Arrange
            var service = new KarpooshehRegistrationService(_context, _secretProtectorMock.Object, _regLoggerMock.Object);
            var req = new KarpooshehRequest
            {
                Name = "شرکت پترو صنعت آریا",
                NationalId = "10103344556",
                Phone = "09121112233",
                PostalCode = "1234567890",
                JobTitle = "مدیر عامل",
                PersonType = PersonType.حقوقی,
                BaleChatId = "chat_auto_1",
                BaleUsername = "aria_bale",
                IsPaid = true
            };
            _context.KarpooshehRequests.Add(req);
            await _context.SaveChangesAsync();

            // Act
            var result = await service.ProvisionUserAndProfileForRequestAsync(req, "123456");

            // Assert
            Assert.NotNull(result.User);
            Assert.NotNull(result.Profile);
            Assert.True(result.CreatedNewUser);
            Assert.True(result.GeneratedNewKeys);

            // بررسی اطلاعات کاربر
            Assert.Equal("09121112233", result.User.Username);
            Assert.Equal("chat_auto_1", result.User.BaleChatId);
            Assert.Equal("aria_bale", result.User.BaleUsername);
            Assert.Equal(PasswordCheckResult.Success, PasswordHashManager.Verify(result.User.Password, "123456"));

            // بررسی پروفایل و کلیدها
            Assert.Equal("شرکت پترو صنعت آریا", result.Profile.FirstName);
            Assert.Equal("10103344556", result.Profile.TinCode);
            Assert.Equal("1234567890", result.Profile.PostCode);
            Assert.StartsWith("ENC:-----BEGIN PRIVATE KEY-----", result.Profile.PrivateKey);
            Assert.StartsWith("-----BEGIN PUBLIC KEY-----", result.Profile.PublicKey);

            // ارتباط مستقیم User با ProfileId
            Assert.Equal(result.Profile.Id, result.User.ProfileId);
        }

        [Fact]
        public async Task StuffWebPage_ToggleFavorite_AddsAndRemovesSuccessfully()
        {
            // Arrange
            var user = new User { Username = "testuser1" };
            _context.Users.Add(user);

            var stuff = new Stuff
            {
                Code = "2720000123456",
                Title = "لوله پلی اتیلن نمونه",
                VatRate = 10,
                Type = StuffType.شناسه_عمومی_تولید_داخل_کالا,
                VatTax = true
            };
            _context.Stuffs.Add(stuff);
            await _context.SaveChangesAsync();

            var loggerMock = new Mock<ILogger<IndexModel>>();
            var pageModel = new IndexModel(_context, _activityLoggerMock.Object, loggerMock.Object);

            var httpContext = new DefaultHttpContext();
            var claims = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser1")
            }, "TestAuth");
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(claims);

            pageModel.PageContext = new PageContext
            {
                HttpContext = httpContext
            };

            // Act 1: افزودن به علاقه‌مندی‌ها
            await pageModel.OnPostToggleFavoriteAsync(stuff.Code, "all", "");

            var favRecord = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.Username == "testuser1" && f.StuffCode == stuff.Code);
            Assert.NotNull(favRecord);

            // Act 2: حذف از علاقه‌مندی‌ها
            await pageModel.OnPostToggleFavoriteAsync(stuff.Code, "all", "");

            var favRecordAfter = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.Username == "testuser1" && f.StuffCode == stuff.Code);
            Assert.Null(favRecordAfter);
        }

        [Fact]
        public async Task StuffWebPage_AddToProducts_CreatesProductEntity()
        {
            // Arrange
            var user = new User { Username = "product_user" };
            _context.Users.Add(user);

            var stuff = new Stuff
            {
                Code = "2720000999888",
                Title = "خدمات تست نرم‌افزار",
                VatRate = 10,
                Type = StuffType.شناسه_عمومی_تولید_داخل_خدمت,
                VatTax = true
            };
            _context.Stuffs.Add(stuff);
            await _context.SaveChangesAsync();

            var loggerMock = new Mock<ILogger<IndexModel>>();
            var pageModel = new IndexModel(_context, _activityLoggerMock.Object, loggerMock.Object);

            var httpContext = new DefaultHttpContext();
            var claims = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "product_user")
            }, "TestAuth");
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(claims);

            pageModel.PageContext = new PageContext
            {
                HttpContext = httpContext
            };

            // Act
            await pageModel.OnPostAddToProductsAsync(stuff.Code, "all", "");

            // Assert
            var product = await _context.Products!.FirstOrDefaultAsync(p => p.Creator == "product_user" && p.Sstid == stuff.Code);
            Assert.NotNull(product);
            Assert.Equal("خدمات تست نرم‌افزار", product.Sstt);
            Assert.Equal(VatRate.درصد_10, product.Vra);
        }
    }
}
