using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using TaxCollectData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice.Tests
{
    public class BaleUserBotServiceTests : IDisposable
    {
        private class TestHttpMessageHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new();
            public List<string> RequestBodies { get; } = new();

            public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":999}}")
            };

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                if (request.Content != null)
                {
                    var body = await request.Content.ReadAsStringAsync(cancellationToken);
                    RequestBodies.Add(body);
                }
                else
                {
                    RequestBodies.Add(string.Empty);
                }
                return ResponseToReturn;
            }
        }

        private readonly SqliteConnection _connection;
        private readonly DataContext _context;
        private readonly Mock<IBaleSettingsService> _settingsServiceMock;
        private readonly Mock<IBaleNotificationService> _adminNotificationServiceMock;
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly Mock<IUserActivityLogger> _activityLoggerMock;
        private readonly Mock<ILogger<BaleUserBotService>> _loggerMock;
        private readonly TestHttpMessageHandler _httpHandler;
        private readonly BaleUserBotService _service;

        public BaleUserBotServiceTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<DataContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new DataContext(options);
            _context.Database.EnsureCreated();

            _settingsServiceMock = new Mock<IBaleSettingsService>();
            _settingsServiceMock.Setup(s => s.GetSettingsAsync())
                .ReturnsAsync(new BaleSettings
                {
                    UserBotEnabled = true,
                    UserBotToken = "test_user_bot_token",
                    UserBotPaymentProviderToken = "WALLET-TEST-1111111111111111",
                    RegistrationFee = 5000000
                });

            _adminNotificationServiceMock = new Mock<IBaleNotificationService>();
            _activityLoggerMock = new Mock<IUserActivityLogger>();
            _loggerMock = new Mock<ILogger<BaleUserBotService>>();

            _httpHandler = new TestHttpMessageHandler();
            var httpClient = new HttpClient(_httpHandler);
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            _service = new BaleUserBotService(
                _context,
                _settingsServiceMock.Object,
                _adminNotificationServiceMock.Object,
                _httpClientFactoryMock.Object,
                _activityLoggerMock.Object,
                _loggerMock.Object);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        private static JsonElement ParseJson(string json)
        {
            return JsonDocument.Parse(json).RootElement;
        }

        private string? GetLastMessageText()
        {
            if (_httpHandler.RequestBodies.Count == 0) return null;
            var last = _httpHandler.RequestBodies[^1];
            using var doc = JsonDocument.Parse(last);
            if (doc.RootElement.TryGetProperty("text", out var t)) return t.GetString();
            if (doc.RootElement.TryGetProperty("error_message", out var err)) return err.GetString();
            return null;
        }

        private List<string> GetAllMessageTexts()
        {
            var list = new List<string>();
            foreach (var b in _httpHandler.RequestBodies)
            {
                try
                {
                    using var doc = JsonDocument.Parse(b);
                    if (doc.RootElement.TryGetProperty("text", out var t))
                    {
                        var str = t.GetString();
                        if (!string.IsNullOrEmpty(str)) list.Add(str);
                    }
                }
                catch { }
            }
            return list;
        }

        [Fact]
        public async Task ProcessUpdateAsync_StartCommand_SendsWelcomeMessageAndInitializesState()
        {
            var update = ParseJson(@"{
                ""update_id"": 1,
                ""message"": {
                    ""message_id"": 10,
                    ""from"": { ""id"": 12345, ""username"": ""tester"", ""first_name"": ""Ali"" },
                    ""chat"": { ""id"": 12345, ""type"": ""private"" },
                    ""text"": ""/start""
                }
            }");

            await _service.ProcessUpdateAsync(update);

            var state = await _context.BaleUserStates.FindAsync("12345");
            Assert.NotNull(state);
            Assert.Equal("None", state.CurrentStep);
            Assert.NotEmpty(_httpHandler.Requests);

            var text = GetLastMessageText();
            Assert.NotNull(text);
            Assert.Contains("خوش آمدید", text);
        }

        [Fact]
        public async Task ProcessUpdateAsync_FullRegistrationWizard_CreatesRequestAndSendsInvoice()
        {
            var chatId = "555123";

            // 1. کلیک روی دکمه ثبت‌نام
            var startRegUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 1,
                    ""from"": {{ ""id"": {chatId}, ""username"": ""johndoe"" }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""📝 ثبت‌نام و دریافت شناسه یکتا""
                }}
            }}");
            await _service.ProcessUpdateAsync(startRegUpdate);

            var state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(state);
            Assert.Equal("Reg_PersonType", state.CurrentStep);

            // 2. انتخاب نوع شخص حقوقی از طریق کال‌بک کوئری
            var ptUpdate = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_1"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""johndoe"" }},
                    ""message"": {{ ""message_id"": 2, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""reg_pt:Legal""
                }}
            }}");
            await _service.ProcessUpdateAsync(ptUpdate);

            state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_Name", state!.CurrentStep);

            // 3. ارسال نام شرکت
            var nameUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 3,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""شرکت نمونه آریا""
                }}
            }}");
            await _service.ProcessUpdateAsync(nameUpdate);

            state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_LatinName", state!.CurrentStep);

            // 4. ارسال نام لاتین معتبر
            var latinUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 4,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""NemonehAria""
                }}
            }}");
            await _service.ProcessUpdateAsync(latinUpdate);

            state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_NationalId", state!.CurrentStep);

            // 5. ارسال شناسه ملی معتبر ۱۱ رقمی
            var natIdUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 5,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""14001234567""
                }}
            }}");
            await _service.ProcessUpdateAsync(natIdUpdate);

            state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_Phone", state!.CurrentStep);

            // 6. ارسال شماره تماس معتبر
            var phoneUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 6,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""09123456789""
                }}
            }}");
            await _service.ProcessUpdateAsync(phoneUpdate);

            state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_JobTitle", state!.CurrentStep);

            // 7. ارسال عنوان فعالیت
            var jobUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 7,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""خدمات بازرگانی و تجارت""
                }}
            }}");
            await _service.ProcessUpdateAsync(jobUpdate);

            state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_UsernameChoice", state!.CurrentStep);

            // 8. انتخاب نام کاربری (شماره همراه)
            var unChoiceUpdate = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_un"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""johndoe"" }},
                    ""message"": {{ ""message_id"": 8, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""reg_un:phone""
                }}
            }}");
            await _service.ProcessUpdateAsync(unChoiceUpdate);

            state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_Confirm", state!.CurrentStep);

            // تایید نهایی و صدور فاکتور پرداخت بله
            var payNowUpdate = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_pay"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""johndoe"" }},
                    ""message"": {{ ""message_id"": 9, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""reg_pay_now""
                }}
            }}");
            await _service.ProcessUpdateAsync(payNowUpdate);

            // راستی‌آزمایی ایجاد رکورد کارپوشه
            var createdReq = await _context.KarpooshehRequests.FirstOrDefaultAsync(r => r.NationalId == "14001234567");
            Assert.NotNull(createdReq);
            Assert.Equal("شرکت نمونه آریا", createdReq.Name);
            Assert.Equal("NemonehAria", createdReq.LatinName);
            Assert.Equal("09123456789", createdReq.Phone);
            Assert.Equal(PersonType.حقوقی, createdReq.PersonType);
            Assert.Equal(chatId, createdReq.BaleChatId);
            Assert.False(createdReq.IsPaid);

            // بررسی ارسال متد sendInvoice به پیام‌رسان بله
            var invoiceReq = _httpHandler.Requests.Find(r => r.RequestUri != null && r.RequestUri.ToString().Contains("sendInvoice"));
            Assert.NotNull(invoiceReq);
        }

        [Fact]
        public async Task ProcessUpdateAsync_LatinName_InvalidFormat_RejectsAndPromptsAgain()
        {
            var chatId = "998877";
            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "Reg_LatinName",
                StateDataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["PersonType"] = "Real",
                    ["Name"] = "علی محمدی"
                }),
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            var updateWithNumbers = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 1,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""Ali123""
                }}
            }}");

            await _service.ProcessUpdateAsync(updateWithNumbers);

            // استیت نباید عوض شود
            var freshState = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_LatinName", freshState!.CurrentStep);

            var text = GetLastMessageText();
            Assert.NotNull(text);
            Assert.Contains("صرفاً شامل حروف انگلیسی", text);
        }

        [Fact]
        public async Task ProcessUpdateAsync_NationalId_InvalidLength_Rejects()
        {
            var chatId = "776655";
            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "Reg_NationalId",
                StateDataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["PersonType"] = "Real",
                    ["Name"] = "علی حسینی",
                    ["LatinName"] = "AliHosseini"
                }),
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            var update = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 1,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""12345""
                }}
            }}");

            await _service.ProcessUpdateAsync(update);

            var freshState = await _context.BaleUserStates.FindAsync(chatId);
            Assert.Equal("Reg_NationalId", freshState!.CurrentStep);

            var text = GetLastMessageText();
            Assert.NotNull(text);
            Assert.Contains("عددی بین ۱۰ تا ۱۱ رقم", text);
        }

        [Fact]
        public async Task ProcessUpdateAsync_PreCheckoutQuery_WhenValidPayload_AnswersOk()
        {
            var update = ParseJson(@"{
                ""update_id"": 99,
                ""pre_checkout_query"": {
                    ""id"": ""pq_12345"",
                    ""from"": { ""id"": 123 },
                    ""currency"": ""IRR"",
                    ""total_amount"": 5000000,
                    ""invoice_payload"": ""REG_456""
                }
            }");

            await _service.ProcessUpdateAsync(update);

            var request = _httpHandler.Requests.Find(r => r.RequestUri != null && r.RequestUri.ToString().Contains("answerPreCheckoutQuery"));
            Assert.NotNull(request);

            var lastBody = _httpHandler.RequestBodies[^1];
            Assert.Contains("\"ok\":true", lastBody);
            Assert.Contains("pq_12345", lastBody);
        }

        [Fact]
        public async Task ProcessUpdateAsync_PreCheckoutQuery_WhenInvalidPayload_AnswersFalse()
        {
            var update = ParseJson(@"{
                ""update_id"": 100,
                ""pre_checkout_query"": {
                    ""id"": ""pq_invalid"",
                    ""from"": { ""id"": 123 },
                    ""currency"": ""IRR"",
                    ""total_amount"": 5000000,
                    ""invoice_payload"": ""UNKNOWN_ORDER_999""
                }
            }");

            await _service.ProcessUpdateAsync(update);

            var text = GetLastMessageText();
            Assert.NotNull(text);
            Assert.Contains("نامعتبر است", text);
        }

        [Fact]
        public async Task ProcessUpdateAsync_SuccessfulPayment_Registration_CompletesPaymentAndNotifiesAdmin()
        {
            var req = new KarpooshehRequest
            {
                Name = "شرکت پویان",
                NationalId = "10101234567",
                Phone = "09121112233",
                JobTitle = "تولیدی بازرگانی",
                Description = "توضیحات تست",
                PersonType = PersonType.حقوقی,
                RequestDate = DateTime.Now,
                BaleChatId = "chat_pay_1",
                IsPaid = false
            };
            _context.KarpooshehRequests.Add(req);
            await _context.SaveChangesAsync();

            var update = ParseJson($@"{{
                ""update_id"": 105,
                ""message"": {{
                    ""message_id"": 50,
                    ""from"": {{ ""id"": 999888, ""username"": ""pouyan_user"" }},
                    ""chat"": {{ ""id"": 999888 }},
                    ""successful_payment"": {{
                        ""currency"": ""IRR"",
                        ""total_amount"": 5000000,
                        ""invoice_payload"": ""REG_{req.Id}"",
                        ""telegram_payment_charge_id"": ""tg_charge_99"",
                        ""provider_payment_charge_id"": ""BALE_PAY_SUCCESS_123""
                    }}
                }}
            }}");

            await _service.ProcessUpdateAsync(update);

            var updatedReq = await _context.KarpooshehRequests.FindAsync(req.Id);
            Assert.NotNull(updatedReq);
            Assert.True(updatedReq.IsPaid);
            Assert.Equal("BALE_PAY_SUCCESS_123", updatedReq.PaymentReference);
            Assert.Equal(5000000, updatedReq.PaymentAmount);

            // نوتیفیکیشن درخواست کارپوشه پرداخت‌شده باید برای ربات ادمین ارسال شود
            _adminNotificationServiceMock.Verify(a => a.SendKarpooshehRequestNotificationAsync(
                It.Is<KarpooshehRequest>(r => r.Id == req.Id && r.IsPaid),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProcessUpdateAsync_SuccessfulPayment_WalletCharge_CreditsUserAccount()
        {
            var user = new User
            {
                Username = "09129998877",
                Profile = new Profile { FirstName = "محمد", Lastname = "تقوی" },
                Vallet = 100000
            };
            _context.Users.Add(user);

            var tx = new Transaction
            {
                CustomerUsername = user.Username,
                Price = 2000000,
                Date = DateTime.Now,
                ByCash = false,
                SourceCardNumber = "6037991122334455",
                Confirm = null
            };
            _context.Transactions.Add(tx);
            await _context.SaveChangesAsync();

            var update = ParseJson($@"{{
                ""update_id"": 106,
                ""message"": {{
                    ""message_id"": 51,
                    ""from"": {{ ""id"": 444333, ""username"": ""mtaghavi"" }},
                    ""chat"": {{ ""id"": 444333 }},
                    ""successful_payment"": {{
                        ""currency"": ""IRR"",
                        ""total_amount"": 2000000,
                        ""invoice_payload"": ""WALLET_{tx.Id}_2000000"",
                        ""provider_payment_charge_id"": ""BALE_WALLET_777""
                    }}
                }}
            }}");

            await _service.ProcessUpdateAsync(update);

            var updatedTx = await _context.Transactions.FindAsync(tx.Id);
            Assert.NotNull(updatedTx);
            Assert.True(updatedTx.Confirm);
            Assert.Equal("BALE_WALLET_777", updatedTx.BankTransactionReceipt);

            var updatedUser = await _context.Users.FindAsync(user.Username);
            Assert.NotNull(updatedUser);
            Assert.Equal(2100000, updatedUser.Vallet); // 100,000 + 2,000,000
        }

        [Fact]
        public async Task ProcessUpdateAsync_AccountLinking_ValidCredentials_LinksChatIdToUser()
        {
            var passwordHash = PasswordHashManager.HashPassword("MySecretPass123");
            var user = new User
            {
                Username = "09121114455",
                Profile = new Profile { FirstName = "حمید", Lastname = "اکبری" },
                Password = passwordHash
            };
            _context.Users.Add(user);

            var state = new BaleUserState
            {
                ChatId = "5566",
                CurrentStep = "Login_Username",
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            // 1. ارسال نام کاربری
            var userUpdate = ParseJson(@"{
                ""message"": {
                    ""message_id"": 1,
                    ""from"": { ""id"": 5566, ""username"": ""hakbari"" },
                    ""chat"": { ""id"": 5566 },
                    ""text"": ""09121114455""
                }
            }");
            await _service.ProcessUpdateAsync(userUpdate);

            var freshState = await _context.BaleUserStates.FindAsync("5566");
            Assert.NotNull(freshState);
            Assert.Equal("Login_Password", freshState.CurrentStep);

            // 2. ارسال رمز عبور صحیح
            var passUpdate = ParseJson(@"{
                ""message"": {
                    ""message_id"": 2,
                    ""from"": { ""id"": 5566, ""username"": ""hakbari"" },
                    ""chat"": { ""id"": 5566 },
                    ""text"": ""MySecretPass123""
                }
            }");
            await _service.ProcessUpdateAsync(passUpdate);

            var linkedUser = await _context.Users.FindAsync("09121114455");
            Assert.NotNull(linkedUser);
            Assert.Equal("5566", linkedUser.BaleChatId);
            Assert.Equal("hakbari", linkedUser.BaleUsername);
        }

        [Fact]
        public async Task NotifyUserTaxIdReadyAsync_SendsFormattedMessageWithCredentials()
        {
            var sent = await _service.NotifyUserTaxIdReadyAsync(
                "chat_target_123",
                "شرکت توسعه پیشرو",
                "ABC123",
                "09125556677",
                "TempPass1234");

            Assert.True(sent);

            var text = GetLastMessageText();
            Assert.NotNull(text);
            Assert.Contains("ABC123", text);
            Assert.Contains("09125556677", text);
            Assert.Contains("TempPass1234", text);
            Assert.Contains("تبریک! شناسه یکتای مالیاتی شما صادر شد", text);
        }

        [Fact]
        public void ConvertHtmlToMarkdown_ConvertsTagsProperly()
        {
            var html = "💰 <b>کیف پول</b>\n• موجودی: <b> 30000 ریال </b>\nکد: <code>09121234567</code>\n<i>راهنما</i>";
            var md = BaleUserBotService.ConvertHtmlToMarkdown(html);

            Assert.DoesNotContain("<b>", md);
            Assert.DoesNotContain("</b>", md);
            Assert.DoesNotContain("<code>", md);
            Assert.DoesNotContain("</code>", md);
            Assert.DoesNotContain("<i>", md);
            Assert.DoesNotContain("</i>", md);

            Assert.Contains("*کیف پول*", md);
            Assert.Contains("*30000 ریال*", md);
            Assert.Contains("`09121234567`", md);
            Assert.Contains("_راهنما_", md);
        }

        [Fact]
        public async Task ProcessUpdateAsync_BackButton_GoesToPreviousStep()
        {
            var chatId = "889911";
            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "Reg_LatinName",
                StateDataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["PersonType"] = "Real",
                    ["Name"] = "رضا احمدی"
                }),
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            var backUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 10,
                    ""from"": {{ ""id"": {chatId} }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""🔙 بازگشت""
                }}
            }}");

            await _service.ProcessUpdateAsync(backUpdate);

            var updatedState = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(updatedState);
            Assert.Equal("Reg_Name", updatedState.CurrentStep);
        }

        [Fact]
        public async Task ProcessUpdateAsync_CallbackCancelReg_CancelsOldRequestAndStartsNew()
        {
            var chatId = "332211";
            var req = new KarpooshehRequest
            {
                Name = "شرکت تستی",
                NationalId = "14009988771",
                Phone = "09123322110",
                JobTitle = "مدیر عامل",
                BaleChatId = chatId,
                IsPaid = false,
                FinishDate = null,
                CancelDate = null
            };
            _context.KarpooshehRequests.Add(req);

            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "None",
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            var cbUpdate = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_cancel_1"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""testuser"" }},
                    ""message"": {{ ""message_id"": 20, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""cancel_reg:{req.Id}""
                }}
            }}");

            await _service.ProcessUpdateAsync(cbUpdate);

            var cancelledReq = await _context.KarpooshehRequests.FindAsync(req.Id);
            Assert.NotNull(cancelledReq);
            Assert.NotNull(cancelledReq.CancelDate);

            var freshState = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(freshState);
            Assert.Equal("Reg_PersonType", freshState.CurrentStep);
        }

        [Fact]
        public async Task GetBaleUsersListAsync_AggregatesUsersRequestsAndStates()
        {
            var user = new User
            {
                Username = "09121110001",
                BaleChatId = "chat_user_1",
                BaleUsername = "userone",
                Profile = new Profile { FirstName = "احمد", Lastname = "رضایی", TinCode = "0011223344" }
            };
            _context.Users.Add(user);

            var req = new KarpooshehRequest
            {
                Name = "شرکت آوا",
                Phone = "09121110002",
                JobTitle = "بازرگانی",
                NationalId = "14001112233",
                BaleChatId = "chat_req_2",
                BaleUsername = "avatest"
            };
            _context.KarpooshehRequests.Add(req);

            var state = new BaleUserState
            {
                ChatId = "chat_bot_3",
                Username = "botuser3",
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            var list = await _service.GetBaleUsersListAsync();

            Assert.NotNull(list);
            Assert.Contains(list, u => u.ChatId == "chat_user_1" && u.Source == "کاربر سامانه");
            Assert.Contains(list, u => u.ChatId == "chat_req_2" && u.Source == "متقاضی کارپوشه");
            Assert.Contains(list, u => u.ChatId == "chat_bot_3" && u.Source == "کاربر ربات");
        }

        [Fact]
        public async Task SendMessageToSelectedUsersAsync_SendsToAllSpecifiedTargets()
        {
            var targets = new List<string> { "chat_t1", "chat_t2", "chat_t3" };

            var sentCount = await _service.SendMessageToSelectedUsersAsync("پیام تستی به افراد انتخاب‌شده", targets);

            Assert.Equal(3, sentCount);
            Assert.True(_httpHandler.Requests.Count >= 3);
        }

        [Fact]
        public async Task ProcessUpdateAsync_ResellerProfileSwitch_ChangesActiveProfile()
        {
            var resellerChatId = "554433";
            var user = new User
            {
                Username = "reseller_user",
                Role = "reseller",
                BaleChatId = resellerChatId
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var prof1 = new Profile { FirstName = "شرکت اول", Lastname = "شعبه ۱", Creator = "reseller_user" };
            var prof2 = new Profile { FirstName = "شرکت دوم", Lastname = "شعبه ۲", Creator = "reseller_user" };
            await _context.Profiles.AddRangeAsync(prof1, prof2);
            await _context.SaveChangesAsync();

            user.ProfileId = prof1.Id;
            user.Profile = prof1;
            await _context.SaveChangesAsync();

            var cbUpdate = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_prof_switch"",
                    ""from"": {{ ""id"": {resellerChatId}, ""username"": ""reseller_user"" }},
                    ""message"": {{ ""message_id"": 99, ""chat"": {{ ""id"": {resellerChatId} }} }},
                    ""data"": ""reseller_prof:{prof2.Id}""
                }}
            }}");

            await _service.ProcessUpdateAsync(cbUpdate);

            var updatedUser = await _context.Users.FindAsync("reseller_user");
            Assert.NotNull(updatedUser);
            // اطمینان از عدم دستکاری ProfileId اصلی کاربر و حفظ پروفایل اصلی فروشنده
            Assert.Equal(prof1.Id, updatedUser.ProfileId);

            // اطمینان از ذخیره پروفایل فعال انتخابی در وضعیت جلسه ربات
            var state = await _context.BaleUserStates.FindAsync(resellerChatId);
            Assert.NotNull(state);
            Assert.Equal(prof2.Id, state.ActiveProfileId);

            var allTexts = GetAllMessageTexts();
            Assert.Contains(allTexts, m => m.Contains("پروفایل فعال با موفقیت تغییر یافت"));
            Assert.Contains(allTexts, m => m.Contains("شرکت دوم شعبه ۲"));
            // اطمینان از عدم ارسال پیام خوش‌آمدگویی پس از انتخاب پروفایل فعال
            Assert.DoesNotContain(allTexts, m => m.Contains("خوش آمدید"));

            // اطمینان از الصاق مستقیم کیبورد اصلی نمایندگی به همان پیام تایید تغییر
            using var lastDoc = JsonDocument.Parse(_httpHandler.RequestBodies[^1]);
            Assert.True(lastDoc.RootElement.TryGetProperty("reply_markup", out var replyMarkup));
            Assert.True(replyMarkup.TryGetProperty("keyboard", out var kb));
            var buttonTexts = new List<string>();
            foreach (var row in kb.EnumerateArray())
            {
                foreach (var btn in row.EnumerateArray())
                {
                    if (btn.TryGetProperty("text", out var tProp))
                    {
                        buttonTexts.Add(tProp.GetString() ?? "");
                    }
                }
            }
            Assert.Contains(buttonTexts, t => t.Contains("مدیریت و انتخاب پروفایل فعال"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_CallbackMainMenu_ResetsStateAndShowsMainMenu()
        {
            var chatId = "11223344";
            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "Favorite_Search",
                StateDataJson = "{\"term\":\"test\"}",
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            var cbUpdate = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_main_menu"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""menu_user"" }},
                    ""message"": {{ ""message_id"": 105, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""main_menu""
                }}
            }}");

            await _service.ProcessUpdateAsync(cbUpdate);

            var updatedState = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(updatedState);
            Assert.Equal("None", updatedState.CurrentStep);
            Assert.Null(updatedState.StateDataJson);

            var allTexts = GetAllMessageTexts();
            Assert.Contains(allTexts, m => m.Contains("خوش آمدید"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_TextBackToMainMenu_ResetsStateAndShowsMainMenu()
        {
            var chatId = "22334455";
            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "Login_Password",
                StateDataJson = "{\"username\":\"someuser\"}",
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            var msgUpdate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 106,
                    ""from"": {{ ""id"": {chatId}, ""username"": ""back_user"" }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""🔙 بازگشت به منوی اصلی""
                }}
            }}");

            await _service.ProcessUpdateAsync(msgUpdate);

            var updatedState = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(updatedState);
            Assert.Equal("None", updatedState.CurrentStep);
            Assert.Null(updatedState.StateDataJson);

            var allTexts = GetAllMessageTexts();
            Assert.Contains(allTexts, m => m.Contains("خوش آمدید"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_Favorites_AddAndRemoveCallbacks_UpdatesDatabase()
        {
            var chatId = "887766";
            var stuff = new Stuff
            {
                Code = "2720000778899",
                Title = "کالای آزمایشی بله",
                VatRate = 10,
                Type = StuffType.شناسه_عمومی_تولید_داخل_کالا
            };
            _context.Stuffs.Add(stuff);

            var user = new User
            {
                Username = "fav_user_1",
                Role = "user",
                BaleChatId = chatId
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // 1. افزودن به علاقه‌مندی‌ها با callback
            var addCb = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_add_fav"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""fav_user_1"" }},
                    ""message"": {{ ""message_id"": 100, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""fav_add:{stuff.Code}""
                }}
            }}");
            await _service.ProcessUpdateAsync(addCb);

            var exists = await _context.UserFavoriteStuffs.AnyAsync(f => f.Username == "fav_user_1" && f.StuffCode == stuff.Code);
            Assert.True(exists);

            // 2. حذف از علاقه‌مندی‌ها با callback
            var delCb = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_del_fav"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""fav_user_1"" }},
                    ""message"": {{ ""message_id"": 101, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""fav_del:{stuff.Code}""
                }}
            }}");
            await _service.ProcessUpdateAsync(delCb);

            var existsAfter = await _context.UserFavoriteStuffs.AnyAsync(f => f.Username == "fav_user_1" && f.StuffCode == stuff.Code);
            Assert.False(existsAfter);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvCreateWizard_StartsStepAndShowsBuyerType()
        {
            var chatId = "33445566";
            var profile = new Profile { FirstName = "شرکت تست مودیان", TinCode = "14001234567", MemoryId = "MEM01" };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "wiz_user",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var update = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_start"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_user"" }},
                    ""message"": {{ ""message_id"": 110, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_create_wizard""
                }}
            }}");

            await _service.ProcessUpdateAsync(update);

            var state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(state);
            Assert.Equal("InvWiz_BuyerType", state.CurrentStep);

            var lastMsg = GetLastMessageText();
            Assert.NotNull(lastMsg);
            Assert.Contains("مرحله ۱ از ۵", lastMsg);
            Assert.Contains("نوع خریدار", lastMsg);
            Assert.Contains("inv_wiz_bt:consumer", _httpHandler.RequestBodies[^1]);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvWizard_BuyerTypeConsumer_TransitionsThroughDateAndPatternToItemSelect()
        {
            var chatId = "44556677";
            var profile = new Profile { FirstName = "فروشگاه نمونه", TinCode = "14009988776", MemoryId = "MEM02" };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "wiz_consumer_user",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id
            };
            _context.Users.Add(user);

            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "InvWiz_BuyerType",
                Username = user.Username,
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            // ۱. انتخاب مصرف‌کننده نهایی -> انتقال به دریافت تاریخ فاکتور
            var updateConsumer = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_consumer"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_consumer_user"" }},
                    ""message"": {{ ""message_id"": 111, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_bt:consumer""
                }}
            }}");

            await _service.ProcessUpdateAsync(updateConsumer);

            var stateDate = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateDate);
            Assert.Equal("InvWiz_Date", stateDate.CurrentStep);

            var dateMsg = GetLastMessageText();
            Assert.NotNull(dateMsg);
            Assert.Contains("تاریخ صورتحساب", dateMsg);
            Assert.Contains("inv_wiz_date_today", _httpHandler.RequestBodies[^1]);

            // ۲. انتخاب تاریخ امروز -> انتقال به انتخاب الگوی صورتحساب
            var updateDate = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_date"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_consumer_user"" }},
                    ""message"": {{ ""message_id"": 112, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_date_today""
                }}
            }}");

            await _service.ProcessUpdateAsync(updateDate);

            var statePattern = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(statePattern);
            Assert.Equal("InvWiz_Pattern", statePattern.CurrentStep);

            var patternMsg = GetLastMessageText();
            Assert.NotNull(patternMsg);
            Assert.Contains("الگوی صورتحساب", patternMsg);
            Assert.Contains("inv_wiz_pattern:1", _httpHandler.RequestBodies[^1]);

            // ۳. انتخاب الگوی اول (فروش) -> انتقال به انتخاب کالا یا خدمت
            var updatePattern = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_pattern"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_consumer_user"" }},
                    ""message"": {{ ""message_id"": 113, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_pattern:1""
                }}
            }}");

            await _service.ProcessUpdateAsync(updatePattern);

            var stateItem = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateItem);
            Assert.Equal("InvWiz_ItemSelect", stateItem.CurrentStep);

            var itemMsg = GetLastMessageText();
            Assert.NotNull(itemMsg);
            Assert.Contains("انتخاب کالا یا خدمت", itemMsg);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvWizard_BuyerTypeLegal_SetsBidAndTinb_And_SkipsTinPrompt()
        {
            var chatId = "44556688";
            var profile = new Profile { FirstName = "شرکت پایا", TinCode = "14001122334", MemoryId = "MEM03" };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "wiz_legal_user",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id
            };
            _context.Users.Add(user);

            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "InvWiz_BuyerType",
                Username = user.Username,
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            // ۱. انتخاب خریدار حقوقی -> درخواست شناسه ملی ۱۱ رقمی
            var cbLegal = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_legal"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_legal_user"" }},
                    ""message"": {{ ""message_id"": 121, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_bt:legal""
                }}
            }}");
            await _service.ProcessUpdateAsync(cbLegal);

            var stateBuyerId = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateBuyerId);
            Assert.Equal("InvWiz_BuyerId", stateBuyerId.CurrentStep);

            // ۲. ارسال شناسه ملی ۱۱ رقمی -> برای حقوقی، شماره اقتصادی همان شناسه ملی است و مستقیم به مرحله تاریخ می‌رود
            var msgBuyerId = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 122,
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""۱۴۰۰۸۸۹۹۰۰۱""
                }}
            }}");
            await _service.ProcessUpdateAsync(msgBuyerId);

            var stateAfterId = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterId);
            Assert.Equal("InvWiz_Date", stateAfterId.CurrentStep);

            // بررسی ذخیره شدن Bid و Tinb برابر با شناسه ملی
            Assert.NotNull(stateAfterId.StateDataJson);
            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stateAfterId.StateDataJson);
            Assert.NotNull(data);
            Assert.Equal("14008899001", data["BuyerId"]);
            Assert.Equal("14008899001", data["BuyerTin"]);

            var lastMsg = GetLastMessageText();
            Assert.NotNull(lastMsg);
            Assert.Contains("تاریخ صورتحساب", lastMsg);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvWizard_BuyerTypeIndividual_AsksForBuyerTin_WithSameIdOption()
        {
            var chatId = "44556699";
            var profile = new Profile { FirstName = "فروشگاه انصاری", TinCode = "14005566778", MemoryId = "MEM04" };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "wiz_indiv_user",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id
            };
            _context.Users.Add(user);

            var state = new BaleUserState
            {
                ChatId = chatId,
                CurrentStep = "InvWiz_BuyerType",
                Username = user.Username,
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            // ۱. انتخاب خریدار حقیقی -> درخواست کد ملی ۱۰ رقمی
            var cbIndiv = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_indiv"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_indiv_user"" }},
                    ""message"": {{ ""message_id"": 131, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_bt:individual""
                }}
            }}");
            await _service.ProcessUpdateAsync(cbIndiv);

            var stateBuyerId = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateBuyerId);
            Assert.Equal("InvWiz_BuyerId", stateBuyerId.CurrentStep);

            // ۲. ارسال کد ملی ۱۰ رقمی -> برای شخص حقیقی، سیستم باید شماره اقتصادی را بپرسد
            var msgBuyerId = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 132,
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""۰۰۱۲۳۴۵۶۷۸""
                }}
            }}");
            await _service.ProcessUpdateAsync(msgBuyerId);

            var stateTin = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateTin);
            Assert.Equal("InvWiz_BuyerTin", stateTin.CurrentStep);

            var tinMsg = GetLastMessageText();
            Assert.NotNull(tinMsg);
            Assert.Contains("شماره اقتصادی خریدار", tinMsg);
            Assert.Contains("inv_wiz_tin_same", _httpHandler.RequestBodies[^1]);

            // ۳. انتخاب «همان کد ملی» -> تنظیم Tinb و انتقال به مرحله تاریخ
            var cbTinSame = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_tin_same"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_indiv_user"" }},
                    ""message"": {{ ""message_id"": 133, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_tin_same""
                }}
            }}");
            await _service.ProcessUpdateAsync(cbTinSame);

            var stateAfterTin = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterTin);
            Assert.Equal("InvWiz_Date", stateAfterTin.CurrentStep);

            Assert.NotNull(stateAfterTin.StateDataJson);
            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stateAfterTin.StateDataJson);
            Assert.NotNull(data);
            Assert.Equal("0012345678", data["BuyerId"]);
            Assert.Equal("0012345678", data["BuyerTin"]);

            // ۴. ارسال تاریخ شمسی دلخواه دستی در چت
            var msgCustomDate = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 134,
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""۱۴۰۳/۰۸/۱۵""
                }}
            }}");
            await _service.ProcessUpdateAsync(msgCustomDate);

            var statePattern = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(statePattern);
            Assert.Equal("InvWiz_Pattern", statePattern.CurrentStep);

            var dataAfterDate = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(statePattern.StateDataJson!);
            Assert.Equal("1403/08/15", dataAfterDate!["InvoiceDate"]);

            // ۵. انتخاب الگوی قرارداد پیمانکاری (الگوی ۴)
            var cbPattern = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_pat_4"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_indiv_user"" }},
                    ""message"": {{ ""message_id"": 135, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_pattern:4""
                }}
            }}");
            await _service.ProcessUpdateAsync(cbPattern);

            var stateItem = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateItem);
            Assert.Equal("InvWiz_ItemSelect", stateItem.CurrentStep);

            var dataAfterPat = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stateItem.StateDataJson!);
            Assert.Equal("4", dataAfterPat!["InvoicePattern"]);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvCancelRecent_ShowsConfirmationCard()
        {
            var chatId = "55667788";
            var profile = new Profile { FirstName = "پروفایل ابطال", TinCode = "10101010101" };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User { Username = "cancel_user", Role = "user", BaleChatId = chatId, ProfileId = profile.Id };
            _context.Users.Add(user);

            var invoice = new Invoices.Models.Invoice
            {
                Sender = user.Username,
                ProfileId = profile.Id,
                Tins = profile.TinCode,
                Taxid = "A111BB222CC333DD444EE5",
                InvoiceDate = "1403/08/10",
                Status = InvoiceStatus.موفق,
                Ins = InvoiceTopic.اصلی
            };
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            var update = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_cancel_recent"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""cancel_user"" }},
                    ""message"": {{ ""message_id"": 112, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_cancel:{invoice.Id}""
                }}
            }}");

            await _service.ProcessUpdateAsync(update);

            var lastMsg = GetLastMessageText();
            Assert.NotNull(lastMsg);
            Assert.Contains("تایید نهایی ابطال صورتحساب", lastMsg);
            Assert.Contains(invoice.Taxid, lastMsg);
            Assert.Contains("رایگان", lastMsg);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvCancelConfirm_CancelsInvoiceSuccessfullyWithoutFee()
        {
            var chatId = "66778899";
            var profile = new Profile { FirstName = "پروفایل تایید ابطال", TinCode = "10102020202" };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var initialWallet = 250000;
            var user = new User
            {
                Username = "confirm_cancel_user",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id,
                Vallet = initialWallet
            };
            _context.Users.Add(user);

            var origInvoice = new Invoices.Models.Invoice
            {
                Sender = user.Username,
                ProfileId = profile.Id,
                Tins = profile.TinCode,
                Taxid = "TAXIDREF99887766554433",
                InvoiceDate = "1403/08/12",
                Status = InvoiceStatus.موفق,
                Ins = InvoiceTopic.اصلی
            };
            _context.Invoices.Add(origInvoice);
            await _context.SaveChangesAsync();

            var senderMock = new Mock<IInvoiceSenderService>();
            senderMock.Setup(s => s.SendInvoice(It.IsAny<Invoices.Models.Invoice>()))
                .Returns(new InvoiceApiHelperResponse("CANCELTAXID1122334455", InvoiceStatus.موفق, "CANCELREF999", "UID999", "OK"));

            var botService = new BaleUserBotService(
                _context,
                _settingsServiceMock.Object,
                _adminNotificationServiceMock.Object,
                _httpClientFactoryMock.Object,
                _activityLoggerMock.Object,
                _loggerMock.Object,
                invoiceSender: senderMock.Object);

            var update = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_confirm_cancel_exec"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""confirm_cancel_user"" }},
                    ""message"": {{ ""message_id"": 113, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_cancel_confirm:{origInvoice.Id}""
                }}
            }}");

            await botService.ProcessUpdateAsync(update);

            // بررسی وضعیت فاکتور اولیه
            var refreshedOrig = await _context.Invoices.FindAsync(origInvoice.Id);
            Assert.NotNull(refreshedOrig);
            Assert.Equal(InvoiceStatus.ابطال_شده, refreshedOrig.Status);
            Assert.NotNull(refreshedOrig.ReferalId);

            // بررسی ایجاد فاکتور ابطالی
            var cancelInv = await _context.Invoices.FindAsync(refreshedOrig.ReferalId.Value);
            Assert.NotNull(cancelInv);
            Assert.Equal(InvoiceTopic.ابطالی, cancelInv.Ins);
            Assert.Equal(origInvoice.Taxid, cancelInv.Irtaxid);
            Assert.Equal("CANCELTAXID1122334455", cancelInv.Taxid);

            // تایید رایگان بودن و عدم کسر ریالی از کیف پول
            var refreshedUser = await _context.Users.FindAsync(user.Username);
            Assert.NotNull(refreshedUser);
            Assert.Equal(initialWallet, refreshedUser.Vallet);

            var allMsgs = GetAllMessageTexts();
            Assert.Contains(allMsgs, m => m.Contains("صورتحساب ابطالی با موفقیت در سامانه مودیان ثبت گردید"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvExcelCancel_RemovesUserDraftInvoices()
        {
            var chatId = "77889900";
            var user = new User { Username = "draft_user", Role = "user", BaleChatId = chatId };
            _context.Users.Add(user);

            var draft1 = new Invoices.Models.Invoice { Sender = user.Username, Tins = "14001234567", Ins = InvoiceTopic.اصلی, Draft = true, Status = InvoiceStatus.در_انتظار_ارسال };
            var draft2 = new Invoices.Models.Invoice { Sender = user.Username, Tins = "14001234567", Ins = InvoiceTopic.اصلی, Draft = true, Status = InvoiceStatus.در_انتظار_ارسال };
            var realInv = new Invoices.Models.Invoice { Sender = user.Username, Tins = "14001234567", Ins = InvoiceTopic.اصلی, Draft = false, Status = InvoiceStatus.موفق };

            await _context.Invoices.AddRangeAsync(draft1, draft2, realInv);
            await _context.SaveChangesAsync();

            var update = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_excel_cancel"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""draft_user"" }},
                    ""message"": {{ ""message_id"": 114, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_excel_cancel""
                }}
            }}");

            await _service.ProcessUpdateAsync(update);

            var remainingDrafts = await _context.Invoices.Where(i => i.Draft && i.Sender == user.Username).ToListAsync();
            Assert.Empty(remainingDrafts);

            var realInvStillExists = await _context.Invoices.AnyAsync(i => i.Id == realInv.Id);
            Assert.True(realInvStillExists);

            var allMsgs = GetAllMessageTexts();
            Assert.Contains(allMsgs, m => m.Contains("لغو و پاکسازی شدند"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_ResellerInvoiceWizard_UsesActiveProfileFromState()
        {
            var resellerChatId = "55443322";
            var user = new User
            {
                Username = "reseller_sender",
                Role = "reseller",
                BaleChatId = resellerChatId,
                Vallet = 500000
            };
            _context.Users.Add(user);

            var mu = new MeasureUnit { Code = "162", Title = "کیلوگرم" };
            _context.MeasureUnits.Add(mu);
            await _context.SaveChangesAsync();

            var prof1 = new Profile { FirstName = "پروفایل اصلی نماینده", Lastname = "شعبه مرکزی", Creator = "reseller_sender", MemoryId = "MEM01", TinCode = "10101010101" };
            var prof2 = new Profile { FirstName = "مشتری انتخابی نماینده", Lastname = "پروژه الف", Creator = "reseller_sender", MemoryId = "MEM02", TinCode = "10202020202" };
            await _context.Profiles.AddRangeAsync(prof1, prof2);
            await _context.SaveChangesAsync();

            user.ProfileId = prof1.Id;
            user.Profile = prof1;
            await _context.SaveChangesAsync();

            var state = new BaleUserState
            {
                ChatId = resellerChatId,
                Username = user.Username,
                CurrentStep = "None",
                ActiveProfileId = prof2.Id,
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            Invoices.Models.Invoice? sentInvoice = null;
            var senderMock = new Mock<IInvoiceSenderService>();
            senderMock.Setup(s => s.SendInvoice(It.IsAny<Invoices.Models.Invoice>()))
                .Callback<Invoices.Models.Invoice>(inv => sentInvoice = inv)
                .Returns(new InvoiceApiHelperResponse("TAXID123456789012345678", InvoiceStatus.موفق, "REF123", "UID123", "OK"));

            var botService = new BaleUserBotService(
                _context,
                _settingsServiceMock.Object,
                _adminNotificationServiceMock.Object,
                _httpClientFactoryMock.Object,
                _activityLoggerMock.Object,
                _loggerMock.Object,
                invoiceSender: senderMock.Object);

            // ارسال فاکتور تکی در گام تایید نهایی
            state.CurrentStep = "InvWiz_Confirm";
            state.StateDataJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["BuyerType"] = "consumer",
                ["ItemCode"] = "1111111111111",
                ["ItemTitle"] = "تست کالا",
                ["Am"] = "2",
                ["Fee"] = "100000",
                ["ItemVat"] = "10",
                ["CheckoutType"] = "1"
            });
            await _context.SaveChangesAsync();

            var cbSend = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_reseller_wiz_send"",
                    ""from"": {{ ""id"": {resellerChatId}, ""username"": ""reseller_sender"" }},
                    ""message"": {{ ""message_id"": 115, ""chat"": {{ ""id"": {resellerChatId} }} }},
                    ""data"": ""inv_wiz_send""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbSend);

            Assert.NotNull(sentInvoice);
            // تایید اینکه فاکتور با مشخصات پروفایل فعال انتخابی صادر و امضا شده است
            Assert.Equal(prof2.Id, sentInvoice.ProfileId);
            Assert.Equal(prof2.TinCode, sentInvoice.Tins);

            // تایید اینکه ProfileId اصلی خود کاربر نماینده تغییر نکرده است
            var updatedUser = await _context.Users.FindAsync(user.Username);
            Assert.NotNull(updatedUser);
            Assert.Equal(prof1.Id, updatedUser.ProfileId);
        }

        [Fact]
        public async Task ProcessUpdateAsync_LogoutFlow_ShowsConfirmation_Cancels_And_ConfirmsLogout()
        {
            var chatId = "889977";
            var prof = new Profile { FirstName = "کاربر خروج", Lastname = "تستی", MemoryId = "MEM_LOGOUT_1" };
            _context.Profiles.Add(prof);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "logout_test_user",
                BaleChatId = chatId,
                ProfileId = prof.Id,
                Profile = prof
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // ۱. کاربر درخواست خروج می‌دهد
            var msgLogout = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 201,
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""🚪 خروج از حساب کاربری""
                }}
            }}");
            await _service.ProcessUpdateAsync(msgLogout);

            var texts = GetAllMessageTexts();
            Assert.Contains(texts, t => t.Contains("خروج از حساب کاربری") && t.Contains("کاربر خروج تستی"));

            // بررسی دکمه‌های تایید و انصراف در کارت شیشه‌ای
            using var docConfirm = JsonDocument.Parse(_httpHandler.RequestBodies[^1]);
            Assert.True(docConfirm.RootElement.TryGetProperty("reply_markup", out var rmConfirm));
            Assert.True(rmConfirm.TryGetProperty("inline_keyboard", out var ikConfirm));
            var cbList = new List<string>();
            foreach (var row in ikConfirm.EnumerateArray())
            {
                foreach (var btn in row.EnumerateArray())
                {
                    if (btn.TryGetProperty("callback_data", out var cd)) cbList.Add(cd.GetString() ?? "");
                }
            }
            Assert.Contains("auth_logout_confirm", cbList);
            Assert.Contains("auth_logout_cancel", cbList);

            // ۲. کاربر انصراف می‌دهد
            var cbCancel = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_cancel_logout"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""logout_test_user"" }},
                    ""message"": {{ ""message_id"": 202, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""auth_logout_cancel""
                }}
            }}");
            await _service.ProcessUpdateAsync(cbCancel);

            var userAfterCancel = await _context.Users.FindAsync("logout_test_user");
            Assert.NotNull(userAfterCancel);
            Assert.Equal(chatId, userAfterCancel.BaleChatId); // هنوز متصل است

            // ۳. کاربر تایید خروج را می‌زند
            var cbConfirm = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_confirm_logout"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""logout_test_user"" }},
                    ""message"": {{ ""message_id"": 203, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""auth_logout_confirm""
                }}
            }}");
            await _service.ProcessUpdateAsync(cbConfirm);

            // اطمینان از پاک شدن BaleChatId کاربر و قطع ارتباط با ربات
            var userAfterLogout = await _context.Users.FindAsync("logout_test_user");
            Assert.NotNull(userAfterLogout);
            Assert.Null(userAfterLogout.BaleChatId);
            Assert.Null(userAfterLogout.BaleUsername);

            var lastTexts = GetAllMessageTexts();
            Assert.Contains(lastTexts, t => t.Contains("خروج از حساب کاربری با موفقیت انجام شد"));

            // بررسی اینکه کیبورد منوی اصلی به حالت ثبت‌نام‌نشده برگشته است
            using var lastDoc = JsonDocument.Parse(_httpHandler.RequestBodies[^1]);
            Assert.True(lastDoc.RootElement.TryGetProperty("reply_markup", out var finalMarkup));
            Assert.True(finalMarkup.TryGetProperty("keyboard", out var finalKb));
            var finalBtns = new List<string>();
            foreach (var row in finalKb.EnumerateArray())
            {
                foreach (var btn in row.EnumerateArray())
                {
                    if (btn.TryGetProperty("text", out var tProp)) finalBtns.Add(tProp.GetString() ?? "");
                }
            }
            Assert.Contains(finalBtns, b => b.Contains("ورود / اتصال به حساب کاربری"));
            Assert.DoesNotContain(finalBtns, b => b.Contains("خروج از حساب"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_Logout_WhenNotLoggedIn_WarnsUser()
        {
            var chatId = "12349988"; // کاربری در دیتابیس با این چت آیدی نیست

            var msg = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 301,
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""/logout""
                }}
            }}");
            await _service.ProcessUpdateAsync(msg);

            var texts = GetAllMessageTexts();
            Assert.Contains(texts, t => t.Contains("شما در حال حاضر به هیچ حساب کاربری متصل نیستید"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_WalletCustomAmount_PromptsAndSendsBaleInvoice()
        {
            var chatId = "771122";
            var prof = new Profile { FirstName = "کاربر شارژ", Lastname = "دلخواه" };
            _context.Profiles.Add(prof);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "wallet_custom_user",
                BaleChatId = chatId,
                ProfileId = prof.Id,
                Profile = prof,
                Vallet = 1000000
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // ۱. کلیک روی دکمه شارژ با مبلغ دلخواه
            var cbCustom = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wallet_custom"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wallet_custom_user"" }},
                    ""message"": {{ ""message_id"": 401, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""wallet_charge_custom""
                }}
            }}");
            await _service.ProcessUpdateAsync(cbCustom);

            var state = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(state);
            Assert.Equal("Wallet_CustomAmount", state.CurrentStep);

            var texts = GetAllMessageTexts();
            Assert.Contains(texts, t => t.Contains("ورود مبلغ دلخواه") && t.Contains("حداقل مبلغ"));

            // ۲. ورود مبلغ نامعتبر کمتر از ۱۰ هزار ریال
            var invalidMsg = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 402,
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""5000""
                }}
            }}");
            await _service.ProcessUpdateAsync(invalidMsg);

            var invalidTexts = GetAllMessageTexts();
            Assert.Contains(invalidTexts, t => t.Contains("مبلغ واردشده نامعتبر است"));

            // استیت باید همچنان در Wallet_CustomAmount باقی بماند
            var stateAfterInvalid = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterInvalid);
            Assert.Equal("Wallet_CustomAmount", stateAfterInvalid.CurrentStep);

            // ۳. ورود مبلغ معتبر با ارقام فارسی و جداکننده هزارگان (۳,۵۰۰,۰۰۰ ریال)
            var validMsg = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 403,
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""۳,۵۰۰,۰۰۰ ریال""
                }}
            }}");
            await _service.ProcessUpdateAsync(validMsg);

            // استیت باید به None برگردد
            var stateAfterValid = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterValid);
            Assert.Equal("None", stateAfterValid.CurrentStep);

            // تراکنش با مبلغ ۳۵۰۰۰۰۰ ریال باید در دیتابیس ثبت شده باشد
            var tx = await _context.Transactions.FirstOrDefaultAsync(t => t.CustomerUsername == "wallet_custom_user");
            Assert.NotNull(tx);
            Assert.Equal(3500000, tx.Price);

            // فاکتور بله باید با مبلغ ۳۵۰۰۰۰۰ به کاربر ارسال شده باشد
            Assert.True(_httpHandler.Requests.Count > 0);
            var invoiceReq = _httpHandler.Requests.Find(r => r.RequestUri != null && r.RequestUri.ToString().Contains("sendInvoice"));
            Assert.NotNull(invoiceReq);
            Assert.Contains(_httpHandler.RequestBodies, b => b.Contains("3500000") && b.Contains($"WALLET_{tx.Id}_3500000"));
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvWizard_Send_ImmediatelyResetsState_ReturnsToMainMenu_And_SetsBidTinbProfile()
        {
            var chatId = "88776655";
            var profile = new Profile
            {
                FirstName = "پروفایل تست ویزارد",
                TinCode = "10103030303",
                MemoryId = "MEM_WIZ_SEND",
                PrivateKey = "MOCK_KEY"
            };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "wiz_full_sender",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id,
                Profile = profile,
                ExpireDate = DateTime.Now.AddDays(30), // اشتراک فعال
                Vallet = 500000
            };
            _context.Users.Add(user);

            var mu = new MeasureUnit { Code = "162", Title = "کیلوگرم" };
            _context.MeasureUnits.Add(mu);

            var state = new BaleUserState
            {
                ChatId = chatId,
                Username = user.Username,
                CurrentStep = "InvWiz_Confirm",
                StateDataJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["BuyerType"] = "individual",
                    ["BuyerId"] = "0012345678",
                    ["BuyerTin"] = "0012345678",
                    ["InvoiceDate"] = "1403/08/15",
                    ["InvoicePattern"] = "4", // قرارداد پیمانکاری
                    ["ItemCode"] = "2720000123456",
                    ["ItemTitle"] = "خدمات تست نرم‌افزاری",
                    ["ItemMu"] = "162",
                    ["Am"] = "3",
                    ["Fee"] = "150000",
                    ["ItemVat"] = "10",
                    ["CheckoutType"] = "1"
                }),
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            Invoices.Models.Invoice? capturedInvoice = null;
            var senderMock = new Mock<IInvoiceSenderService>();
            senderMock.Setup(s => s.SendInvoice(It.IsAny<Invoices.Models.Invoice>()))
                .Callback<Invoices.Models.Invoice>(inv => capturedInvoice = inv)
                .Returns(new InvoiceApiHelperResponse("TAXID12345678901234567", InvoiceStatus.موفق, "REF_123", "UID_123", "موفق"));

            var botService = new BaleUserBotService(
                _context,
                _settingsServiceMock.Object,
                _adminNotificationServiceMock.Object,
                _httpClientFactoryMock.Object,
                _activityLoggerMock.Object,
                _loggerMock.Object,
                invoiceSender: senderMock.Object);

            var cbSend = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_wiz_send_full"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""wiz_full_sender"" }},
                    ""message"": {{ ""message_id"": 501, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_send""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbSend);

            // ۱. بررسی ریست فوری استیت ویزارد
            var updatedState = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(updatedState);
            Assert.Equal("None", updatedState.CurrentStep);
            Assert.Null(updatedState.StateDataJson);

            // ۲. بررسی پیام‌های ارسالی به کاربر (پیام صف با کیبورد منوی اصلی و سپس پیام موفقیت مودیان)
            var texts = GetAllMessageTexts();
            Assert.Contains(texts, t => t.Contains("در صف ارسال به سامانه مودیان قرار گرفت"));
            Assert.Contains(texts, t => t.Contains("با موفقیت به سامانه مودیان ارسال شد") && t.Contains("TAXID12345678901234567"));

            // ۳. بررسی فیلدهای فاکتور ارسال‌شده به سرویس مودیان
            Assert.NotNull(capturedInvoice);
            Assert.Equal(profile.Id, capturedInvoice.ProfileId);
            Assert.NotNull(capturedInvoice.Profile);
            Assert.Equal(profile.Id, capturedInvoice.Profile.Id);
            Assert.Equal("0012345678", capturedInvoice.Bid);
            Assert.Equal("0012345678", capturedInvoice.Tinb);
            Assert.Equal(InvoicePattern.قرارداد_پیمانکاری, capturedInvoice.Inp);
            Assert.Equal("1403/08/15", capturedInvoice.InvoiceDate);
            Assert.NotNull(capturedInvoice.Indatim);
            Assert.Equal(InvoiceStatus.موفق, capturedInvoice.Status);
            Assert.Equal("TAXID12345678901234567", capturedInvoice.Taxid);

            // ۴. بررسی دیتابیس
            var dbInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Taxid == "TAXID12345678901234567");
            Assert.NotNull(dbInvoice);
            Assert.Equal("0012345678", dbInvoice.Bid);
            Assert.Equal("0012345678", dbInvoice.Tinb);
            Assert.Equal(InvoicePattern.قرارداد_پیمانکاری, dbInvoice.Inp);
            Assert.Equal("1403/08/15", dbInvoice.InvoiceDate);
        }

        [Fact]
        public async Task ProcessUpdateAsync_SendExcelDraftInvoices_ImmediatelyReturnsToMainMenu_And_SendsInvoices()
        {
            var chatId = "88776644";
            var profile = new Profile
            {
                FirstName = "پروفایل اکسل",
                TinCode = "10104040404",
                MemoryId = "MEM_EXCEL_SEND",
                PrivateKey = "MOCK_KEY"
            };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "excel_draft_sender",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id,
                Profile = profile,
                ExpireDate = DateTime.Now.AddDays(30),
                Vallet = 500000
            };
            _context.Users.Add(user);

            var draft1 = new Invoices.Models.Invoice
            {
                Sender = user.Username,
                ProfileId = profile.Id,
                Profile = profile,
                Tins = profile.TinCode,
                Draft = true,
                Status = InvoiceStatus.در_انتظار_ارسال,
                InvoiceDate = "1403/08/14",
                Ins = InvoiceTopic.اصلی,
                Inp = InvoicePattern.فروش,
                Inty = InvoiceType.نوع_اول,
                Setm = CheckoutType.نقدی
            };
            var draft2 = new Invoices.Models.Invoice
            {
                Sender = user.Username,
                ProfileId = profile.Id,
                Profile = profile,
                Tins = profile.TinCode,
                Draft = true,
                Status = InvoiceStatus.در_انتظار_ارسال,
                InvoiceDate = "1403/08/14",
                Ins = InvoiceTopic.اصلی,
                Inp = InvoicePattern.فروش,
                Inty = InvoiceType.نوع_اول,
                Setm = CheckoutType.نقدی
            };
            await _context.Invoices.AddRangeAsync(draft1, draft2);
            await _context.SaveChangesAsync();

            var sentCount = 0;
            var senderMock = new Mock<IInvoiceSenderService>();
            senderMock.Setup(s => s.SendInvoice(It.IsAny<Invoices.Models.Invoice>()))
                .Callback<Invoices.Models.Invoice>(inv => sentCount++)
                .Returns(new InvoiceApiHelperResponse("TAXID_BATCH_123", InvoiceStatus.موفق, "REF_B", "UID_B", "موفق"));

            var botService = new BaleUserBotService(
                _context,
                _settingsServiceMock.Object,
                _adminNotificationServiceMock.Object,
                _httpClientFactoryMock.Object,
                _activityLoggerMock.Object,
                _loggerMock.Object,
                invoiceSender: senderMock.Object);

            var cbConfirm = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_excel_confirm_test"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""excel_draft_sender"" }},
                    ""message"": {{ ""message_id"": 601, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_excel_confirm""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbConfirm);

            var texts = GetAllMessageTexts();
            Assert.Contains(texts, t => t.Contains("تعداد 2 صورتحساب در صف ارسال به سامانه مودیان قرار گرفت"));
            Assert.Contains(texts, t => t.Contains("گزارش ارسال فاکتورها به سامانه مودیان") && t.Contains("صورتحساب‌های موفق:") && t.Contains("2 عدد"));

            Assert.Equal(2, sentCount);
            var updatedDrafts = await _context.Invoices.Where(i => i.Sender == user.Username).ToListAsync();
            Assert.All(updatedDrafts, d => Assert.False(d.Draft));
            Assert.All(updatedDrafts, d => Assert.Equal(InvoiceStatus.موفق, d.Status));
        }

        [Fact]
        public void InvoiceApiHelper_GetLastStatus_WhenUidAndRefAreEmpty_ReturnsInformativeResponse()
        {
            var profile = new Profile { MemoryId = "MEM_TEST", PrivateKey = "KEY_TEST" };
            var invoice = new Invoices.Models.Invoice
            {
                Id = 27133,
                Profile = profile,
                UID = null,
                ReferenceCode = "",
                Status = InvoiceStatus.در_انتظار_ارسال,
                Taxid = "TAXID_TEST_EMPTY_REF"
            };

            var helper = new TaxCollectData.InvoiceApiHelper();
            var response = helper.GetLastStatus(invoice);

            Assert.NotNull(response);
            Assert.Equal(InvoiceStatus.در_انتظار_ارسال, response.Status);
            Assert.Contains("صورتحساب هنوز به سامانه مودیان ارسال نشده است", response.Response);
        }

        [Fact]
        public void InvoiceApiHelper_GetLastStatus_WhenProfileIsNull_ReturnsWarningResponse()
        {
            var invoice = new Invoices.Models.Invoice
            {
                Id = 27133,
                Profile = null,
                UID = "SOME_UID",
                ReferenceCode = "SOME_REF",
                Status = InvoiceStatus.در_حال_ارسال,
                Taxid = "TAXID_NO_PROFILE"
            };

            var helper = new TaxCollectData.InvoiceApiHelper();
            var response = helper.GetLastStatus(invoice);

            Assert.NotNull(response);
            Assert.Equal(InvoiceStatus.در_حال_ارسال, response.Status);
            Assert.Contains("پروفایل صادرکننده مشخص نیست", response.Response);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvCancelConfirm_WithScopeFactory_ReturnsImmediatelyAndSendsInBackground()
        {
            var chatId = "99887766";
            var profile = new Profile
            {
                FirstName = "پروفایل ابطال پس‌زمینه",
                TinCode = "10105050505",
                MemoryId = "MEM_CANCEL_BG",
                PrivateKey = "MOCK_KEY"
            };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "cancel_bg_user",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id,
                Profile = profile,
                Vallet = 200000
            };
            _context.Users.Add(user);

            var origInvoice = new Invoices.Models.Invoice
            {
                Sender = user.Username,
                ProfileId = profile.Id,
                Profile = profile,
                Tins = profile.TinCode,
                Taxid = "TAXID_ORIG_998877",
                InvoiceDate = "1403/08/15",
                Status = InvoiceStatus.موفق,
                Ins = InvoiceTopic.اصلی
            };
            _context.Invoices.Add(origInvoice);
            await _context.SaveChangesAsync();

            var sentCancel = false;
            var senderMock = new Mock<IInvoiceSenderService>();
            senderMock.Setup(s => s.SendInvoice(It.IsAny<Invoices.Models.Invoice>()))
                .Callback<Invoices.Models.Invoice>(inv => sentCancel = true)
                .Returns(new InvoiceApiHelperResponse("TAXID_CANCEL_998877", InvoiceStatus.موفق, "REF_C", "UID_C", "موفق"));

            var serviceProviderMock = new Mock<IServiceProvider>();
            var scopeMock = new Mock<IServiceScope>();
            var scopeFactoryMock = new Mock<IServiceScopeFactory>();

            scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
            scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

            serviceProviderMock.Setup(sp => sp.GetService(typeof(DataContext))).Returns(_context);
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IInvoiceSenderService))).Returns(senderMock.Object);
            serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<BaleUserBotService>))).Returns(_loggerMock.Object);
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IUserActivityLogger))).Returns(_activityLoggerMock.Object);

            BaleUserBotService? botServiceInScope = null;
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IBaleUserBotService))).Returns(() =>
            {
                botServiceInScope ??= new BaleUserBotService(
                    _context,
                    _settingsServiceMock.Object,
                    _adminNotificationServiceMock.Object,
                    _httpClientFactoryMock.Object,
                    _activityLoggerMock.Object,
                    _loggerMock.Object,
                    invoiceSender: senderMock.Object);
                return botServiceInScope;
            });

            var botService = new BaleUserBotService(
                _context,
                _settingsServiceMock.Object,
                _adminNotificationServiceMock.Object,
                _httpClientFactoryMock.Object,
                _activityLoggerMock.Object,
                _loggerMock.Object,
                scopeFactory: scopeFactoryMock.Object,
                invoiceSender: senderMock.Object);

            var cbConfirm = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_cancel_bg_test"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""cancel_bg_user"" }},
                    ""message"": {{ ""message_id"": 701, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_cancel_confirm:{origInvoice.Id}""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbConfirm);

            // بررسی پیام فوری ثبت درخواست و شروع در پس‌زمینه
            var texts = GetAllMessageTexts();
            Assert.Contains(texts, t => t.Contains("درخواست صدور صورتحساب ابطالی ثبت گردید") && t.Contains("TAXID_ORIG_998877"));

            // منتظر شدن برای اتمام تسک پس‌زمینه
            for (int i = 0; i < 20; i++)
            {
                if (sentCancel) break;
                await Task.Delay(100);
            }

            Assert.True(sentCancel);
            var refreshedOrig = await _context.Invoices.FindAsync(origInvoice.Id);
            Assert.NotNull(refreshedOrig);
            Assert.Equal(InvoiceStatus.ابطال_شده, refreshedOrig.Status);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvWizard_MultiItem_AddNextItem_And_Continue_SendsMultiItemInvoice()
        {
            var chatId = "1122334455";
            var profile = new Profile
            {
                FirstName = "فروشگاه چندقلمی",
                TinCode = "14001234567",
                MemoryId = "MEM_MULTI_1",
                PrivateKey = "MOCK_KEY"
            };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            var user = new User
            {
                Username = "multi_item_user",
                Role = "user",
                BaleChatId = chatId,
                ProfileId = profile.Id,
                Profile = profile,
                ExpireDate = DateTime.Now.AddDays(30),
                Vallet = 1000000
            };
            _context.Users.Add(user);

            var stuff1 = new Stuff { Code = "2720000000001", Title = "کالای اول آزمایشی", VatRate = 10 };
            var stuff2 = new Stuff { Code = "2720000000002", Title = "کالای دوم آزمایشی", VatRate = 10 };
            _context.Stuffs.AddRange(stuff1, stuff2);

            var mu = new MeasureUnit { Code = "162", Title = "کیلوگرم" };
            _context.MeasureUnits.Add(mu);

            var state = new BaleUserState
            {
                ChatId = chatId,
                Username = user.Username,
                CurrentStep = "InvWiz_Fee",
                StateDataJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["BuyerType"] = "consumer",
                    ["InvoiceDate"] = "1403/08/20",
                    ["InvoicePattern"] = "1",
                    ["ItemCode"] = stuff1.Code,
                    ["ItemTitle"] = stuff1.Title,
                    ["ItemVat"] = "10",
                    ["ItemMu"] = "162",
                    ["Am"] = "2"
                }),
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            Invoices.Models.Invoice? capturedInvoice = null;
            var senderMock = new Mock<IInvoiceSenderService>();
            senderMock.Setup(s => s.SendInvoice(It.IsAny<Invoices.Models.Invoice>()))
                .Callback<Invoices.Models.Invoice>(inv => capturedInvoice = inv)
                .Returns(new InvoiceApiHelperResponse("TAXID_MULTI_1234567890", InvoiceStatus.موفق, "REF_MULTI", "UID_MULTI", "موفق"));

            var botService = new BaleUserBotService(
                _context,
                _settingsServiceMock.Object,
                _adminNotificationServiceMock.Object,
                _httpClientFactoryMock.Object,
                _activityLoggerMock.Object,
                _loggerMock.Object,
                invoiceSender: senderMock.Object);

            // ۱. کاربر فی قلم اول را ۱۰۰,۰۰۰ ریال ارسال می‌کند
            var updateFee1 = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 601,
                    ""from"": {{ ""id"": {chatId}, ""username"": ""multi_item_user"" }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""100000""
                }}
            }}");

            await botService.ProcessUpdateAsync(updateFee1);

            // بررسی انتقال به وضعیت InvWiz_ItemRegistered
            var stateAfterFee1 = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterFee1);
            Assert.Equal("InvWiz_ItemRegistered", stateAfterFee1.CurrentStep);

            var registeredMsg1 = GetLastMessageText();
            Assert.NotNull(registeredMsg1);
            Assert.Contains("با موفقیت به پیش‌نویس فاکتور اضافه شد", registeredMsg1);
            Assert.Contains("کالای اول آزمایشی", registeredMsg1);
            Assert.Contains("inv_wiz_add_next_item", _httpHandler.RequestBodies[^1]);
            Assert.Contains("inv_wiz_continue", _httpHandler.RequestBodies[^1]);

            // ۲. کاربر دکمه «افزودن کالای بعدی» را می‌زند
            var cbAddNext = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_add_next_1"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""multi_item_user"" }},
                    ""message"": {{ ""message_id"": 602, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_add_next_item""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbAddNext);

            var stateAfterAddNext = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterAddNext);
            Assert.Equal("InvWiz_ItemSelect", stateAfterAddNext.CurrentStep);

            // ۳. کاربر کالای دوم را انتخاب می‌کند
            var cbSelectItem2 = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_item_2"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""multi_item_user"" }},
                    ""message"": {{ ""message_id"": 603, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_item:{stuff2.Code}""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbSelectItem2);

            var stateAfterItem2 = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterItem2);
            Assert.Equal("InvWiz_Quantity", stateAfterItem2.CurrentStep);

            // ۴. کاربر تعداد کالای دوم را ۵ وارد می‌کند
            var updateAm2 = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 604,
                    ""from"": {{ ""id"": {chatId}, ""username"": ""multi_item_user"" }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""5""
                }}
            }}");

            await botService.ProcessUpdateAsync(updateAm2);

            var stateAfterAm2 = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterAm2);
            Assert.Equal("InvWiz_Fee", stateAfterAm2.CurrentStep);

            // ۵. کاربر فی کالای دوم را ۲۰۰,۰۰۰ ریال وارد می‌کند
            var updateFee2 = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 605,
                    ""from"": {{ ""id"": {chatId}, ""username"": ""multi_item_user"" }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""200000""
                }}
            }}");

            await botService.ProcessUpdateAsync(updateFee2);

            var stateAfterFee2 = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterFee2);
            Assert.Equal("InvWiz_ItemRegistered", stateAfterFee2.CurrentStep);

            var registeredMsg2 = GetLastMessageText();
            Assert.NotNull(registeredMsg2);
            Assert.Contains("۲ قلم", registeredMsg2);
            Assert.Contains("کالای اول آزمایشی", registeredMsg2);
            Assert.Contains("کالای دوم آزمایشی", registeredMsg2);

            // ۶. کاربر روی «ادامه و تکمیل فاکتور» کلیک می‌کند
            var cbContinue = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_continue_multi"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""multi_item_user"" }},
                    ""message"": {{ ""message_id"": 606, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_continue""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbContinue);

            // برای خریدار مصرف‌کننده نهایی، مستقیماً به کارت تایید فاکتور می‌رود
            var stateConfirm = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateConfirm);
            Assert.Equal("InvWiz_Confirm", stateConfirm.CurrentStep);

            var confirmMsg = GetLastMessageText();
            Assert.NotNull(confirmMsg);
            Assert.Contains("پیش‌نمایش و تایید نهایی صورتحساب", confirmMsg);
            Assert.Contains("اقلام صورتحساب (۲ قلم)", confirmMsg);
            Assert.Contains("کالای اول آزمایشی", confirmMsg);
            Assert.Contains("کالای دوم آزمایشی", confirmMsg);

            // ۷. تایید و ارسال نهایی فاکتور به سامانه مودیان
            var cbSend = ParseJson($@"{{
                ""callback_query"": {{
                    ""id"": ""cb_send_multi"",
                    ""from"": {{ ""id"": {chatId}, ""username"": ""multi_item_user"" }},
                    ""message"": {{ ""message_id"": 607, ""chat"": {{ ""id"": {chatId} }} }},
                    ""data"": ""inv_wiz_send""
                }}
            }}");

            await botService.ProcessUpdateAsync(cbSend);

            // ۸. اعتبارسنجی صورتحساب دریافتی توسط سرویس ارسال مودیان
            Assert.NotNull(capturedInvoice);
            Assert.Equal(2, capturedInvoice.InvoiceItems.Count);

            var item1 = capturedInvoice.InvoiceItems.FirstOrDefault(i => i.Sstid == stuff1.Code);
            var item2 = capturedInvoice.InvoiceItems.FirstOrDefault(i => i.Sstid == stuff2.Code);

            Assert.NotNull(item1);
            Assert.Equal(2, item1.Am);
            Assert.Equal(100000, item1.Fee);

            Assert.NotNull(item2);
            Assert.Equal(5, item2.Am);
            Assert.Equal(200000, item2.Fee);

            // استیت کاربر ریست شده باشد
            var stateEnd = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateEnd);
            Assert.Equal("None", stateEnd.CurrentStep);
        }

        [Fact]
        public async Task ProcessUpdateAsync_InvWizard_MultiItem_GoBackInItemRegistered_PopsLastItem()
        {
            var chatId = "9988112233";
            var user = new User { Username = "back_multi_user", BaleChatId = chatId, Role = "user" };
            _context.Users.Add(user);

            var items = new List<WizardInvoiceItem>
            {
                new WizardInvoiceItem { Code = "111", Title = "قلم اول", Am = 1, Fee = 50000, VatRate = 10 },
                new WizardInvoiceItem { Code = "222", Title = "قلم دوم", Am = 3, Fee = 120000, VatRate = 10 }
            };

            var state = new BaleUserState
            {
                ChatId = chatId,
                Username = user.Username,
                CurrentStep = "InvWiz_ItemRegistered",
                StateDataJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["BuyerType"] = "consumer",
                    ["ItemsJson"] = System.Text.Json.JsonSerializer.Serialize(items)
                }),
                LastUpdated = DateTime.Now
            };
            _context.BaleUserStates.Add(state);
            await _context.SaveChangesAsync();

            // کاربر دکمه بازگشت را در صفحه اقلام ثبت‌شده لمس می‌کند
            var updateBack = ParseJson($@"{{
                ""message"": {{
                    ""message_id"": 620,
                    ""from"": {{ ""id"": {chatId}, ""username"": ""back_multi_user"" }},
                    ""chat"": {{ ""id"": {chatId} }},
                    ""text"": ""🔙 بازگشت""
                }}
            }}");

            await _service.ProcessUpdateAsync(updateBack);

            var stateAfterBack = await _context.BaleUserStates.FindAsync(chatId);
            Assert.NotNull(stateAfterBack);
            Assert.Equal("InvWiz_Fee", stateAfterBack.CurrentStep);

            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stateAfterBack.StateDataJson!);
            Assert.NotNull(data);
            Assert.Equal("222", data["ItemCode"]);
            Assert.Equal("120000", data["Fee"]);
            Assert.Equal("3", data["Am"]);

            // در ItemsJson فقط قلم اول باقی مانده است
            var remainingItems = System.Text.Json.JsonSerializer.Deserialize<List<WizardInvoiceItem>>(data["ItemsJson"]);
            Assert.NotNull(remainingItems);
            Assert.Single(remainingItems);
            Assert.Equal("111", remainingItems[0].Code);
        }
    }
}
