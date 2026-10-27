using System;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class BaleBotActionServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DataContext _context;
        private readonly Mock<IUserActivityLogger> _activityLoggerMock;
        private readonly Mock<ILogger<BaleBotActionService>> _loggerMock;
        private readonly BaleBotActionService _service;

        public BaleBotActionServiceTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<DataContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new DataContext(options);
            _context.Database.EnsureCreated();

            _activityLoggerMock = new Mock<IUserActivityLogger>();
            _loggerMock = new Mock<ILogger<BaleBotActionService>>();

            _service = new BaleBotActionService(_context, _activityLoggerMock.Object, _loggerMock.Object);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        [Fact]
        public async Task HandleActionAsync_WalletApprove_CreditsUserAndConfirmsTransaction()
        {
            var user = new User { Username = "user1", Vallet = 50000 };
            _context.Users.Add(user);

            var trans = new Transaction
            {
                Id = 10,
                CustomerUsername = "user1",
                Price = 100000,
                Date = DateTime.Now,
                SourceCardNumber = "6037991122334455",
                Confirm = null
            };
            _context.Transactions.Add(trans);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("wallet", "approve", 10, "BaleAdmin");

            Assert.True(result.Success);
            Assert.Contains("تایید", result.Message);

            var updatedUser = await _context.Users.FindAsync("user1");
            Assert.Equal(150000, updatedUser!.Vallet);

            var updatedTrans = await _context.Transactions.FindAsync(10);
            Assert.True(updatedTrans!.Confirm);
            Assert.NotNull(updatedTrans.ConfirmDate);
        }

        [Fact]
        public async Task HandleActionAsync_WalletApprove_WhenAlreadyConfirmed_ReturnsWarningWithoutDoubleCrediting()
        {
            var user = new User { Username = "user2", Vallet = 200000 };
            _context.Users.Add(user);

            var trans = new Transaction
            {
                Id = 11,
                CustomerUsername = "user2",
                Price = 100000,
                Confirm = true,
                ConfirmDate = DateTime.Now.AddDays(-1),
                SourceCardNumber = "6037991122334455"
            };
            _context.Transactions.Add(trans);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("wallet", "approve", 11, "BaleAdmin");

            Assert.False(result.Success);
            Assert.True(result.IsWarning);
            Assert.Contains("قبلاً", result.Message);

            var updatedUser = await _context.Users.FindAsync("user2");
            Assert.Equal(200000, updatedUser!.Vallet);
        }

        [Fact]
        public async Task HandleActionAsync_WalletReject_RemovesTransaction()
        {
            var user = new User { Username = "user3", Vallet = 50000 };
            _context.Users.Add(user);

            var trans = new Transaction
            {
                Id = 12,
                CustomerUsername = "user3",
                Price = 50000,
                Confirm = null,
                SourceCardNumber = "6037991122334455"
            };
            _context.Transactions.Add(trans);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("wallet", "reject", 12, "BaleAdmin");

            Assert.True(result.Success);
            Assert.Contains("رد شد", result.Message);

            var found = await _context.Transactions.FindAsync(12);
            Assert.Null(found);
        }

        [Fact]
        public async Task HandleActionAsync_WalletReject_WhenAlreadyConfirmed_ReturnsFail()
        {
            var user = new User { Username = "user4", Vallet = 50000 };
            _context.Users.Add(user);

            var trans = new Transaction
            {
                Id = 13,
                CustomerUsername = "user4",
                Price = 50000,
                Confirm = true,
                SourceCardNumber = "6037991122334455"
            };
            _context.Transactions.Add(trans);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("wallet", "reject", 13, "BaleAdmin");

            Assert.False(result.Success);
            Assert.Contains("قبلاً تایید شده", result.Message);
        }

        [Fact]
        public async Task HandleActionAsync_KarpooshehApprove_SetsReceiveDate()
        {
            var req = new KarpooshehRequest
            {
                Id = 20,
                Name = "شرکت تست آریا",
                NationalId = "10101010101",
                Phone = "09123456789",
                JobTitle = "مدیر عامل",
                RequestDate = DateTime.Now
            };
            _context.KarpooshehRequests.Add(req);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("karpoosheh", "approve", 20, "BaleAdmin");

            Assert.True(result.Success);
            var updated = await _context.KarpooshehRequests.FindAsync(20);
            Assert.NotNull(updated!.ReceiveDate);
        }

        [Fact]
        public async Task HandleActionAsync_KarpooshehApprove_WhenAlreadyReceived_ReturnsWarning()
        {
            var req = new KarpooshehRequest
            {
                Id = 21,
                Name = "شرکت تست دو",
                NationalId = "10101010102",
                Phone = "09123456788",
                JobTitle = "مدیر",
                ReceiveDate = DateTime.Now.AddDays(-2)
            };
            _context.KarpooshehRequests.Add(req);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("karpoosheh", "approve", 21, "BaleAdmin");

            Assert.False(result.Success);
            Assert.True(result.IsWarning);
            Assert.Contains("قبلاً", result.Message);
        }

        [Fact]
        public async Task HandleActionAsync_KarpooshehReject_SetsCancelAndFinishDate()
        {
            var req = new KarpooshehRequest
            {
                Id = 22,
                Name = "متقاضی سه",
                NationalId = "1234567890",
                Phone = "09120000001",
                JobTitle = "کسبه"
            };
            _context.KarpooshehRequests.Add(req);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("karpoosheh", "reject", 22, "BaleAdmin");

            Assert.True(result.Success);
            var updated = await _context.KarpooshehRequests.FindAsync(22);
            Assert.NotNull(updated!.CancelDate);
            Assert.NotNull(updated.FinishDate);
        }

        [Fact]
        public async Task HandleActionAsync_CreditApprove_ExtendsUserExpiryAndRemovesRequest()
        {
            var user = new User
            {
                Username = "userCredit1",
                ExpireDate = DateTime.Now.AddDays(10)
            };
            _context.Users.Add(user);

            var period = new CreditPeriod
            {
                Id = 1,
                Title = "پلن ۳ ماهه",
                Months = 3,
                Price = 300000
            };
            _context.CreditPeriods.Add(period);

            var credit = new CreditRequest
            {
                Id = 30,
                Username = "userCredit1",
                PeriodId = 1,
                Months = 3,
                Price = 300000,
                Date = DateTime.Now
            };
            _context.CreditRequests.Add(credit);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("credit", "approve", 30, "BaleAdmin");

            Assert.True(result.Success);
            var updatedUser = await _context.Users.FindAsync("userCredit1");
            Assert.True(updatedUser!.ExpireDate > DateTime.Now.AddMonths(2));

            var foundCredit = await _context.CreditRequests.FindAsync(30);
            Assert.Null(foundCredit);
        }

        [Fact]
        public async Task HandleActionAsync_CreditReject_RemovesRequest()
        {
            var period = new CreditPeriod
            {
                Id = 2,
                Title = "پلن ۱ ماهه",
                Months = 1,
                Price = 100000
            };
            _context.CreditPeriods.Add(period);

            var credit = new CreditRequest
            {
                Id = 31,
                Username = "userCredit2",
                PeriodId = 2,
                Months = 1,
                Date = DateTime.Now
            };
            _context.CreditRequests.Add(credit);
            await _context.SaveChangesAsync();

            var result = await _service.HandleActionAsync("credit", "reject", 31, "BaleAdmin");

            Assert.True(result.Success);
            var found = await _context.CreditRequests.FindAsync(31);
            Assert.Null(found);
        }

        [Fact]
        public async Task ProcessTextCommandAsync_PersianCommands_DispatchesCorrectly()
        {
            var user = new User { Username = "userFa", Vallet = 10000 };
            _context.Users.Add(user);

            var trans = new Transaction
            {
                Id = 99,
                CustomerUsername = "userFa",
                Price = 50000,
                SourceCardNumber = "1234123412341234"
            };
            _context.Transactions.Add(trans);
            await _context.SaveChangesAsync();

            var result = await _service.ProcessTextCommandAsync("تایید کیف پول 99", "12345", "admin");

            Assert.True(result.Success);
            var updatedUser = await _context.Users.FindAsync("userFa");
            Assert.Equal(60000, updatedUser!.Vallet);
        }

        [Fact]
        public async Task ProcessTextCommandAsync_HelpAndStatus_ReturnValidMessages()
        {
            var help = await _service.ProcessTextCommandAsync("/help", "12345");
            Assert.True(help.Success);
            Assert.Contains("ربات مدیریت سامانه", help.Message);

            var status = await _service.ProcessTextCommandAsync("/status", "12345");
            Assert.True(status.Success);
            Assert.Contains("فعال است", status.Message);
        }
    }
}