using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// سیاست امنیتی پیش‌فرض: تمام صفحات نیازمند احراز هویت هستند و فقط صفحاتی که در پایین
// صریحاً استثنا شده‌اند (صفحه اصلی، خطا، ورود، ثبت‌نام و فرم عمومی درخواست) بدون لاگین در دسترس‌اند.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Privacy");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Register");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Account/Logout");
    options.Conventions.AllowAnonymousToPage("/Request/RequestTaxMemoryId");
    options.Conventions.AllowAnonymousToPage("/Request/SentSuccess");
}).AddRazorRuntimeCompilation();
builder.Services.AddControllers(); // Enables MVC
var connection = builder.Configuration.GetConnectionString("default");
builder.Services.AddDbContext<DataContext>(options =>
{
    options.UseSqlServer(connection, sqlOptions =>
    {
        // تاب‌آوری اتصال (Connection Resiliency) برای جلوگیری از خطاهای زودگذر شبکه و قطع ناگهانی هندشیک SQL
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: new[] { 0, 10054, 10053, 4060, 40197, 40501, 40613, 49918, 49919, 49920, 11001 });
        sqlOptions.CommandTimeout(60);
    });
});
builder.Services.AddScoped<ISelectedProfile, SelectedProfile>();
builder.Services.AddScoped<IImportTemplateService, ImportTemplateService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IScopedFields, ScopedFields>();
builder.Services.AddScoped<ICustomerFinanceService, CustomerFinanceService>();
builder.Services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();
builder.Services.AddScoped<IUserActivityLogger, UserActivityLogger>();
builder.Services.AddHttpContextAccessor();

// خدمات اتصال و اطلاع‌رسانی پیام‌رسان بله
builder.Services.AddHttpClient("BaleClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IBaleSettingsService, BaleSettingsService>();
builder.Services.AddScoped<IBaleNotificationService, BaleNotificationService>();
builder.Services.AddScoped<IBaleBotActionService, BaleBotActionService>();
builder.Services.AddScoped<IBaleUpdateProcessor, BaleUpdateProcessor>();
builder.Services.AddHostedService<BaleBotBackgroundService>();
builder.Services.AddScoped<IBaleUserBotService, BaleUserBotService>();
builder.Services.AddHostedService<BaleUserBotBackgroundService>();

// محدودسازی تلاش‌های ناموفق ورود (محافظت در برابر بروت‌فورس)
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ILoginThrottle, LoginThrottle>();

// رمزنگاری داده‌های حساس مودیان (کلید خصوصی امضای مالیاتی) پیش از ذخیره در پایگاه داده.
// حلقه کلیدها بیرون از پوشه برنامه نگهداری می‌شود تا با انتشار مجدد از بین نرود.
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (string.IsNullOrWhiteSpace(keyRingPath))
{
    keyRingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Invoice-DataProtection-Keys");
}
builder.Services.AddDataProtection()
    .SetApplicationName("Invoice")
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
builder.Services.AddScoped<IProfileSecretProtector, ProfileSecretProtector>();
builder.Services.AddScoped<IKarpooshehRegistrationService, KarpooshehRegistrationService>();

builder.Services.AddSession(p =>
{
    p.IdleTimeout = TimeSpan.FromHours(3);

    p.Cookie.HttpOnly = true;
    p.Cookie.IsEssential = true;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(op =>
{
    op.LoginPath = "/Account/login";
    op.AccessDeniedPath = "/Account/AccessDenied";
    op.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    op.SlidingExpiration = true;

}
);
var app = builder.Build();

// اطمینان از وجود کاربر مدیر در اولین اجرا.
// رمز عبور اولیه از تنظیمات خوانده می‌شود و فقط در زمان ایجاد حساب اعمال می‌گردد،
// بنابراین تغییر رمز توسط مدیر با هر بار استارت برنامه از بین نمی‌رود.
try
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<DataContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    // اطمینان از وجود جدول لاگ فعالیت کاربران در پایگاه داده
    try
    {
        context.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'[UserActivityLogs]', 'U') IS NULL
BEGIN
    CREATE TABLE [UserActivityLogs] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Timestamp] DATETIME2 NOT NULL,
        [Username] NVARCHAR(100) NOT NULL,
        [UserRole] NVARCHAR(50) NULL,
        [ProfileId] INT NULL,
        [ProfileTitle] NVARCHAR(200) NULL,
        [Category] NVARCHAR(100) NOT NULL,
        [Action] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(MAX) NOT NULL,
        [EntityId] NVARCHAR(100) NULL,
        [IpAddress] NVARCHAR(50) NULL,
        [Status] NVARCHAR(50) NOT NULL
    );
    CREATE NONCLUSTERED INDEX [IX_UserActivityLogs_Timestamp] ON [UserActivityLogs] ([Timestamp] DESC);
    CREATE NONCLUSTERED INDEX [IX_UserActivityLogs_Category] ON [UserActivityLogs] ([Category]);
    CREATE NONCLUSTERED INDEX [IX_UserActivityLogs_Username] ON [UserActivityLogs] ([Username]);
END");
    }
    catch (Exception exTable)
    {
        startupLogger.LogWarning(exTable, "بررسی جدول UserActivityLogs با خطا مواجه شد.");
    }

    // اطمینان از وجود جدول وضعیت گفتگوی کاربران بله و ستون‌های تکمیلی
    try
    {
        context.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'[BaleUserStates]', 'U') IS NULL
BEGIN
    CREATE TABLE [BaleUserStates] (
        [ChatId] NVARCHAR(100) NOT NULL PRIMARY KEY,
        [Username] NVARCHAR(100) NULL,
        [CurrentStep] NVARCHAR(100) NOT NULL,
        [StateDataJson] NVARCHAR(MAX) NULL,
        [LastUpdated] DATETIME2 NOT NULL
    );
END
IF COL_LENGTH('Users', 'BaleChatId') IS NULL ALTER TABLE Users ADD BaleChatId NVARCHAR(100) NULL;
IF COL_LENGTH('Users', 'BaleUsername') IS NULL ALTER TABLE Users ADD BaleUsername NVARCHAR(100) NULL;
IF COL_LENGTH('KarpooshehRequests', 'PostalCode') IS NULL ALTER TABLE KarpooshehRequests ADD PostalCode NVARCHAR(50) NULL;
IF COL_LENGTH('KarpooshehRequests', 'BaleChatId') IS NULL ALTER TABLE KarpooshehRequests ADD BaleChatId NVARCHAR(100) NULL;
IF COL_LENGTH('KarpooshehRequests', 'BaleUsername') IS NULL ALTER TABLE KarpooshehRequests ADD BaleUsername NVARCHAR(100) NULL;
IF COL_LENGTH('KarpooshehRequests', 'IsPaid') IS NULL ALTER TABLE KarpooshehRequests ADD IsPaid BIT NOT NULL CONSTRAINT DF_Karpoosheh_IsPaid DEFAULT 0;
IF COL_LENGTH('KarpooshehRequests', 'PaymentReference') IS NULL ALTER TABLE KarpooshehRequests ADD PaymentReference NVARCHAR(100) NULL;
IF COL_LENGTH('KarpooshehRequests', 'PaymentAmount') IS NULL ALTER TABLE KarpooshehRequests ADD PaymentAmount INT NULL;
IF COL_LENGTH('Profiles', 'PublicKey') IS NULL ALTER TABLE Profiles ADD PublicKey NVARCHAR(MAX) NULL;
IF OBJECT_ID(N'[UserFavoriteStuffs]', 'U') IS NULL
BEGIN
    CREATE TABLE [UserFavoriteStuffs] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Username] NVARCHAR(100) NOT NULL,
        [StuffCode] NVARCHAR(100) NOT NULL,
        [AddedDate] DATETIME2 NOT NULL DEFAULT GETDATE()
    );
    CREATE UNIQUE INDEX [IX_UserFavoriteStuffs_User_Stuff] ON [UserFavoriteStuffs] ([Username], [StuffCode]);
END
");
    }
    catch (Exception exSchema)
    {
        startupLogger.LogWarning(exSchema, "بررسی ستون‌ها و جدول‌های ربات بله با خطا مواجه شد.");
    }

    var initialAdminPassword = builder.Configuration["InitialAdminPassword"] ?? "la#1631";
    var user = context.Users.SingleOrDefault(p => p.Username == "admin");
    if (user == null)
    {
        user = new User
        {
            Username = "admin",
            Password = PasswordHashManager.HashPassword(initialAdminPassword),
            Role = "admin"
        };
        context.Users.Add(user);
        context.SaveChanges();
        startupLogger.LogWarning("حساب مدیر در اولین اجرا ایجاد شد. رمز عبور اولیه را بلافاصله تغییر دهید.");
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "خطا در مقداردهی اولیه پایگاه داده هنگام آغاز برنامه. ادامه اجرا بدون مقداردهی اولیه.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change it for production scenarios, see https://aka.ms/aspnetcore-hsts.
    //app.UseHsts();
}
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None,
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
});

//app.UseHttpsRedirection();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.MapRazorPages();
app.MapControllers();

app.Run();
