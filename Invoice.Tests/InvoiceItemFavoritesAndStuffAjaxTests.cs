using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Invoices.Pages.InvoiceManage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using StuffIndexModel = Invoice.Pages.StuffPages.IndexModel;

namespace Invoice.Tests
{
    public class InvoiceItemFavoritesAndStuffAjaxTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DataContext _context;
        private readonly Mock<IUserActivityLogger> _activityLoggerMock;
        private readonly Mock<ILogger<StuffIndexModel>> _indexLoggerMock;

        public InvoiceItemFavoritesAndStuffAjaxTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<DataContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new DataContext(options);
            _context.Database.EnsureCreated();

            // در دیتابیس اصلی SQL Server، جدول UserFavoriteStuffs بدون کلید خارجی روی StuffCode است تا کالاهای اختصاصی مشتری
            // (که در جدول ۳ میلیونی Stuffs نیستند) بتوانند در منتخب‌ها ذخیره شوند. در SQLite برای شبیه‌سازی دقیق همین رفتار
            // بررسی کلید خارجی را غیرفعال می‌کنیم.
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys = OFF;";
                cmd.ExecuteNonQuery();
            }

            _activityLoggerMock = new Mock<IUserActivityLogger>();
            _indexLoggerMock = new Mock<ILogger<StuffIndexModel>>();
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        private static PageContext CreatePageContext(string username, bool isAjax = false)
        {
            var httpContext = new DefaultHttpContext();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, username)
            }, "TestAuth");
            httpContext.User = new ClaimsPrincipal(identity);

            if (isAjax)
            {
                httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
            }

            return new PageContext
            {
                HttpContext = httpContext,
                ViewData = new ViewDataDictionary<StuffIndexModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            };
        }

        [Fact]
        public async Task InvoiceItems_GetProducts_ReturnsOnlyCurrentUserFavoriteStuffs()
        {
            // Arrange
            var user1 = "mouddi_1";
            var user2 = "mouddi_2";

            _context.Users.Add(new User { Username = user1 });
            _context.Users.Add(new User { Username = user2 });

            var stuff1 = new Stuff { Code = "2720000000001", Title = "لوله پلیکا ۲۰", VatRate = 10, VatTax = true, Type = StuffType.شناسه_اختصاصی_تولید_داخل_کالا };
            var stuff2 = new Stuff { Code = "2720000000002", Title = "اتصالات پلی اتیلن", VatRate = 9, VatTax = true, Type = StuffType.شناسه_عمومی_تولید_داخل_کالا };
            var stuff3 = new Stuff { Code = "2720000000003", Title = "کالای کاربر دیگر", VatRate = 10, VatTax = true, Type = StuffType.شناسه_عمومی_وارداتی_کالا };
            var stuffNonFav = new Stuff { Code = "2720000000004", Title = "کالای عمومی غیر منتخب", VatRate = 10, VatTax = true };

            await _context.Stuffs.AddRangeAsync(stuff1, stuff2, stuff3, stuffNonFav);

            // منتخب‌های کاربر ۱: stuff1 و stuff2
            await _context.UserFavoriteStuffs.AddAsync(new UserFavoriteStuff { Username = user1, StuffCode = stuff1.Code, AddedDate = DateTime.Now.AddMinutes(-5) });
            await _context.UserFavoriteStuffs.AddAsync(new UserFavoriteStuff { Username = user1, StuffCode = stuff2.Code, AddedDate = DateTime.Now });

            // منتخب کاربر ۲: stuff3
            await _context.UserFavoriteStuffs.AddAsync(new UserFavoriteStuff { Username = user2, StuffCode = stuff3.Code, AddedDate = DateTime.Now });

            // کالای ذخیره شده در Products توسط ادمین (نباید بدون منتخب ظاهر شود)
            await _context.Products.AddAsync(new Product { Sstid = "9999999999999", Sstt = "کالای عمومی ادمین", Creator = "admin" });

            await _context.SaveChangesAsync();

            var invoiceItemsModel = new InvoiceItemsModel(_context, _activityLoggerMock.Object)
            {
                PageContext = CreatePageContext(user1)
            };

            // Act: دریافت کلیه محصولات کاربر ۱ بدون فیلتر جستجو
            var result = await invoiceItemsModel.OnGetGetProductsAsync("");

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            var jsonString = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            // باید دقیقاً ۲ قلم کالای منتخب کاربر ۱ بازگردانده شود
            Assert.Equal(2, root.GetArrayLength());

            var returnedCodes = root.EnumerateArray().Select(el => el.GetProperty("sstid").GetString()).ToList();
            Assert.Contains(stuff1.Code, returnedCodes);
            Assert.Contains(stuff2.Code, returnedCodes);
            Assert.DoesNotContain(stuff3.Code, returnedCodes);
            Assert.DoesNotContain(stuffNonFav.Code, returnedCodes);
            Assert.DoesNotContain("9999999999999", returnedCodes);
        }

        [Fact]
        public async Task InvoiceItems_GetProducts_FiltersBySearchTermOnTitleOrCode()
        {
            // Arrange
            var user = "search_user";
            _context.Users.Add(new User { Username = user });

            var s1 = new Stuff { Code = "2720000111111", Title = "کابل برق افشان", VatRate = 10, VatTax = true };
            var s2 = new Stuff { Code = "2720000222222", Title = "سیم مفتولی مسی", VatRate = 10, VatTax = true };
            await _context.Stuffs.AddRangeAsync(s1, s2);
            await _context.UserFavoriteStuffs.AddAsync(new UserFavoriteStuff { Username = user, StuffCode = s1.Code });
            await _context.UserFavoriteStuffs.AddAsync(new UserFavoriteStuff { Username = user, StuffCode = s2.Code });
            await _context.SaveChangesAsync();

            var model = new InvoiceItemsModel(_context, _activityLoggerMock.Object)
            {
                PageContext = CreatePageContext(user)
            };

            // Act: جستجو بر اساس کلمه «افشان»
            var result = await model.OnGetGetProductsAsync("افشان");

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            var jsonString = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            Assert.Equal(1, root.GetArrayLength());
            Assert.Equal(s1.Code, root[0].GetProperty("sstid").GetString());
        }

        [Fact]
        public async Task StuffIndex_OnGet_WithAjax_ReturnsPartialViewResult()
        {
            // Arrange
            var user = "ajax_user";
            _context.Users.Add(new User { Username = user });
            await _context.SaveChangesAsync();

            var pageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user, isAjax: true),
                Tab = "all"
            };

            // Act
            var result = await pageModel.OnGetAsync();

            // Assert
            var partialResult = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("_StuffTablePartial", partialResult.ViewName);
            Assert.Equal(pageModel, partialResult.Model);
        }

        [Fact]
        public async Task StuffIndex_ToggleFavorite_Ajax_ReturnsJsonWithUpdatedStatus()
        {
            // Arrange
            var user = "toggle_ajax_user";
            _context.Users.Add(new User { Username = user });

            var stuff = new Stuff { Code = "2720000555555", Title = "کالای آزمایشی آژاکس", VatRate = 10, VatTax = true };
            await _context.Stuffs.AddAsync(stuff);
            await _context.SaveChangesAsync();

            var pageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user, isAjax: true)
            };

            // Act 1: افزودن به علاقه‌مندی‌ها با درخواست آژاکس
            var resultAdd = await pageModel.OnPostToggleFavoriteAsync(stuff.Code, "all", "");

            // Assert 1
            var jsonAdd = Assert.IsType<JsonResult>(resultAdd);
            var jsonStringAdd = JsonSerializer.Serialize(jsonAdd.Value);
            using var docAdd = JsonDocument.Parse(jsonStringAdd);
            var rootAdd = docAdd.RootElement;

            Assert.True(rootAdd.GetProperty("success").GetBoolean());
            Assert.True(rootAdd.GetProperty("isFavorite").GetBoolean());
            Assert.Equal(1, rootAdd.GetProperty("favoritesCount").GetInt32());
            Assert.Equal(stuff.Code, rootAdd.GetProperty("code").GetString());

            // Act 2: لغو علاقه‌مندی با درخواست آژاکس
            var resultRemove = await pageModel.OnPostToggleFavoriteAsync(stuff.Code, "all", "");

            // Assert 2
            var jsonRemove = Assert.IsType<JsonResult>(resultRemove);
            var jsonStringRemove = JsonSerializer.Serialize(jsonRemove.Value);
            using var docRemove = JsonDocument.Parse(jsonStringRemove);
            var rootRemove = docRemove.RootElement;

            Assert.True(rootRemove.GetProperty("success").GetBoolean());
            Assert.False(rootRemove.GetProperty("isFavorite").GetBoolean());
            Assert.Equal(0, rootRemove.GetProperty("favoritesCount").GetInt32());
        }

        [Fact]
        public async Task StuffIndex_AddToProducts_Ajax_ReturnsJsonSuccess()
        {
            // Arrange
            var user = "add_product_ajax_user";
            _context.Users.Add(new User { Username = user });

            var stuff = new Stuff { Code = "2720000777777", Title = "شیرآلات صنعتی آژاکس", VatRate = 10, VatTax = true, Type = StuffType.شناسه_اختصاصی_تولید_داخل_کالا };
            await _context.Stuffs.AddAsync(stuff);
            await _context.SaveChangesAsync();

            var pageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user, isAjax: true)
            };

            // Act: ثبت کالا در لیست من
            var result = await pageModel.OnPostAddToProductsAsync(stuff.Code, "all", "");

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            var jsonString = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(stuff.Code, root.GetProperty("code").GetString());

            // بررسی ایجاد در دیتابیس
            var savedProduct = await _context.Products.FirstOrDefaultAsync(p => p.Creator == user && p.Sstid == stuff.Code);
            Assert.NotNull(savedProduct);
            Assert.Equal("شیرآلات صنعتی آژاکس", savedProduct.Sstt);
        }

        [Fact]
        public async Task StuffIndex_UpdateProductDetails_CreatesProductAndAddsToFavorites_WhenNotExists()
        {
            // Arrange
            var user = "update_product_user";
            _context.Users.Add(new User { Username = user });

            var mu = new MeasureUnit { Code = "162", Title = "کیلوگرم" };
            var stuff = new Stuff { Code = "2720000888888", Title = "ورق روغنی فولادی", VatRate = 10, VatTax = true, Type = StuffType.شناسه_اختصاصی_تولید_داخل_کالا };
            await _context.MeasureUnits.AddAsync(mu);
            await _context.Stuffs.AddAsync(stuff);
            await _context.SaveChangesAsync();

            var pageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user, isAjax: true)
            };

            // Act: تنظیم قیمت و واحد سنجش برای کالایی که قبلاً تنظیم نشده بود
            var result = await pageModel.OnPostUpdateProductDetailsAsync(stuff.Code, 2500000m, "162", "ورق روغنی سفارشی");

            // Assert: خروجی JSON
            var jsonResult = Assert.IsType<JsonResult>(result);
            var jsonString = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(stuff.Code, root.GetProperty("code").GetString());
            Assert.Equal("ورق روغنی سفارشی", root.GetProperty("title").GetString());
            Assert.Equal(2500000m, root.GetProperty("fee").GetDecimal());
            Assert.Equal("162", root.GetProperty("mu").GetString());
            Assert.Equal("کیلوگرم", root.GetProperty("muTitle").GetString());
            Assert.Equal(1, root.GetProperty("favoritesCount").GetInt32());

            // Assert: وضعیت در پایگاه داده
            var savedProd = await _context.Products.FirstOrDefaultAsync(p => p.Creator == user && p.Sstid == stuff.Code);
            Assert.NotNull(savedProd);
            Assert.Equal(2500000m, savedProd.Fee);
            Assert.Equal("162", savedProd.Mu);
            Assert.Equal("ورق روغنی سفارشی", savedProd.Sstt);

            var savedFav = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.Username == user && f.StuffCode == stuff.Code);
            Assert.NotNull(savedFav);
        }

        [Fact]
        public async Task StuffIndex_UpdateProductDetails_UpdatesExistingProductAndSyncsWithFavorites()
        {
            // Arrange
            var user = "edit_existing_product_user";
            _context.Users.Add(new User { Username = user });

            var mu1 = new MeasureUnit { Code = "162", Title = "کیلوگرم" };
            var mu2 = new MeasureUnit { Code = "163", Title = "تن" };
            var stuff = new Stuff { Code = "2720000999999", Title = "میلگرد آجدار A3", VatRate = 10, VatTax = true };
            var prod = new Product
            {
                Creator = user,
                Sstid = stuff.Code,
                Sstt = stuff.Title,
                Fee = 1800000m,
                Mu = "162"
            };
            await _context.MeasureUnits.AddRangeAsync(mu1, mu2);
            await _context.Stuffs.AddAsync(stuff);
            await _context.Products.AddAsync(prod);
            await _context.SaveChangesAsync();

            var pageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user, isAjax: true)
            };

            // Act: تغییر واحد به تن و افزایش قیمت به ۳۵,۰۰۰,۰۰۰ ریال
            var result = await pageModel.OnPostUpdateProductDetailsAsync(stuff.Code, 35000000m, "163", "میلگرد A3 شاخه‌ای");

            // Assert: خروجی JSON
            var jsonResult = Assert.IsType<JsonResult>(result);
            var jsonString = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(35000000m, root.GetProperty("fee").GetDecimal());
            Assert.Equal("163", root.GetProperty("mu").GetString());
            Assert.Equal("تن", root.GetProperty("muTitle").GetString());

            // Assert: ویرایش موجودیت دیتابیس بدون ایجاد رکورد تکراری
            var count = await _context.Products.CountAsync(p => p.Creator == user && p.Sstid == stuff.Code);
            Assert.Equal(1, count);

            var updatedProd = await _context.Products.FirstAsync(p => p.Creator == user && p.Sstid == stuff.Code);
            Assert.Equal(35000000m, updatedProd.Fee);
            Assert.Equal("163", updatedProd.Mu);
            Assert.Equal("میلگرد A3 شاخه‌ای", updatedProd.Sstt);

            // بررسی خودکار اضافه شدن به علاقه‌مندی‌ها
            var fav = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.Username == user && f.StuffCode == stuff.Code);
            Assert.NotNull(fav);
        }

        [Fact]
        public async Task InvoiceItems_GetProducts_PopulatesConfiguredFeeAndMeasureUnitFromProduct()
        {
            // Arrange
            var user = "invoice_user_with_prices";
            _context.Users.Add(new User { Username = user });

            var mu = new MeasureUnit { Code = "168", Title = "خدمت" };
            var stuff = new Stuff { Code = "2720000123456", Title = "خدمات نصب و راه‌اندازی", VatRate = 10, VatTax = true, Type = StuffType.شناسه_عمومی_تولید_داخل_خدمت };
            var product = new Product
            {
                Creator = user,
                Sstid = stuff.Code,
                Sstt = "خدمات تخصصی نصب",
                Fee = 15000000m,
                Mu = "168"
            };

            await _context.MeasureUnits.AddAsync(mu);
            await _context.Stuffs.AddAsync(stuff);
            await _context.Products.AddAsync(product);
            await _context.UserFavoriteStuffs.AddAsync(new UserFavoriteStuff { Username = user, StuffCode = stuff.Code });
            await _context.SaveChangesAsync();

            var invoiceItemsModel = new InvoiceItemsModel(_context, _activityLoggerMock.Object)
            {
                PageContext = CreatePageContext(user)
            };

            // Act
            var result = await invoiceItemsModel.OnGetGetProductsAsync("");

            // Assert: اعتبارسنجی انتقال دقیق قیمت و واحد تنظیم‌شده به دراپ‌داون اقلام فاکتور
            var jsonResult = Assert.IsType<JsonResult>(result);
            var jsonString = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            Assert.Equal(1, root.GetArrayLength());
            var item = root[0];
            Assert.Equal(stuff.Code, item.GetProperty("sstid").GetString());
            Assert.Equal("خدمات تخصصی نصب", item.GetProperty("sstt").GetString());
            Assert.Equal(15000000m, item.GetProperty("fee").GetDecimal());
            Assert.Equal("168", item.GetProperty("mu").GetString());
        }

        [Fact]
        public async Task StuffIndex_OnGet_WhenFavoritesEmpty_KeepsFavTab_AndShowsEmptyWithoutSwitchingToAll()
        {
            // Arrange
            var user = "empty_fav_user";
            _context.Users.Add(new User { Username = user });

            // وجود کالای عمومی در جدول کاتالوگ سازمان (۳ میلیون کالا)
            var publicStuff = new Stuff { Code = "2720000999001", Title = "کالای عمومی مالیات", VatRate = 10, VatTax = true };
            await _context.Stuffs.AddAsync(publicStuff);
            await _context.SaveChangesAsync();

            var pageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user)
            };

            // Act: ورود به صفحه با تب پیش‌فرض
            await pageModel.OnGetAsync();

            // Assert: تب باید حتماً روی "fav" بماند و به "all" سوییچ نشود
            Assert.Equal("fav", pageModel.Tab);
            Assert.Equal(0, pageModel.FavoritesCount);
            Assert.Equal(0, pageModel.TotalCount);
            Assert.Empty(pageModel.Items);
        }

        [Fact]
        public async Task StuffIndex_OnGet_ShowsExistingCustomerProducts_EvenIfMissingFromStuffsCatalog()
        {
            // Arrange
            var user = "custom_prod_user";
            _context.Users.Add(new User { Username = user });

            var mu = new MeasureUnit { Code = "162", Title = "کیلوگرم" };
            await _context.MeasureUnits.AddAsync(mu);

            // کالای اختصاصی مشتری در جدول Products که در جدول عمومی Stuffs وجود ندارد
            var customCode = "CUSTOM99988877";
            var prod = new Product
            {
                Creator = user,
                Sstid = customCode,
                Sstt = "کالای دست‌ساز اختصاصی مشتری",
                Fee = 450000m,
                Mu = "162"
            };
            await _context.Products.AddAsync(prod);
            await _context.UserFavoriteStuffs.AddAsync(new UserFavoriteStuff
            {
                Username = user,
                StuffCode = customCode,
                AddedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();

            var pageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user)
            };

            // Act
            await pageModel.OnGetAsync();

            // Assert: کالای اختصاصی باید در تب کالاهای منتخب ظاهر شود حتی بدون رکورد در Stuffs
            Assert.Equal(1, pageModel.FavoritesCount);
            Assert.Single(pageModel.Items);
            var item = pageModel.Items[0];
            Assert.Equal(customCode, item.Code);
            Assert.Equal("کالای دست‌ساز اختصاصی مشتری", item.Title);
            Assert.Equal(450000m, item.Fee);
            Assert.Equal("162", item.Mu);
        }

        [Fact]
        public async Task ProductCreate_OnPost_DoesNotAddToPublicStuffsTable_AndIsIsolatedToCurrentUser()
        {
            // Arrange
            var user1 = "mouddi_owner";
            var user2 = "mouddi_other";
            _context.Users.Add(new User { Username = user1 });
            _context.Users.Add(new User { Username = user2 });

            var mu = new MeasureUnit { Code = "162", Title = "کیلوگرم" };
            await _context.MeasureUnits.AddAsync(mu);
            await _context.SaveChangesAsync();

            var createModel = new Invoice.Pages_Product.CreateModel(_context)
            {
                PageContext = CreatePageContext(user1),
                Product = new Product
                {
                    Sstid = "NEW_CUSTOM_123",
                    Sstt = "محصول تعریف شده توسط مودی ۱",
                    Fee = 780000m,
                    Mu = "162"
                }
            };

            // Act: ثبت محصول جدید
            var result = await createModel.OnPostAsync();

            // Assert 1: هدایت به تب کالاهای منتخب
            var redirectResult = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Stuff/Index", redirectResult.PageName);
            Assert.Equal("fav", redirectResult.RouteValues?["Tab"]);

            // Assert 2: کالا نباید در جدول عمومی Stuffs ثبت شود
            var inPublicStuffs = await _context.Stuffs.AnyAsync(s => s.Code == "NEW_CUSTOM_123");
            Assert.False(inPublicStuffs, "کالای اختصاصی کاربر نباید در جدول کاتالوگ عمومی Stuffs درج گردد");

            // Assert 3: کالا باید در Products با مالکیت کاربر ۱ و منتخب‌های او وجود داشته باشد
            var inProducts = await _context.Products.FirstOrDefaultAsync(p => p.Sstid == "NEW_CUSTOM_123");
            Assert.NotNull(inProducts);
            Assert.Equal(user1, inProducts.Creator);

            var inFavorites = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.StuffCode == "NEW_CUSTOM_123" && f.Username == user1);
            Assert.NotNull(inFavorites);

            // Assert 4: کاربر دوم در صفحه کالاهای منتخب خود این کالا را نمی‌بیند
            var otherPageModel = new StuffIndexModel(_context, _activityLoggerMock.Object, _indexLoggerMock.Object)
            {
                PageContext = CreatePageContext(user2)
            };
            await otherPageModel.OnGetAsync();
            Assert.DoesNotContain(otherPageModel.Items, s => s.Code == "NEW_CUSTOM_123");
        }
    }
}
