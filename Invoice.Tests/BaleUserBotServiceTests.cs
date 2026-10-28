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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
            Assert.Equal(prof2.Id, updatedUser.ProfileId);

            var allTexts = GetAllMessageTexts();
            Assert.Contains(allTexts, m => m.Contains("پروفایل فعال با موفقیت تغییر یافت"));
            Assert.Contains(allTexts, m => m.Contains("شرکت دوم شعبه ۲"));
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
    }
}
