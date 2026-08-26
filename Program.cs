using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages().AddRazorRuntimeCompilation();
builder.Services.AddControllers(); // Enables MVC
var connection=builder.Configuration.GetConnectionString("default");
builder.Services.AddDbContext<DataContext>(p => p.UseSqlServer("server=185.252.29.62,2016;database=sodooref_invoice;user id=sodooref_sa;password=ali@2240841;"));
builder.Services.AddScoped<ISelectedProfile,SelectedProfile>();
builder.Services.AddScoped<IImportTemplateService, ImportTemplateService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IScopedFields,ScopedFields>();
builder.Services.AddScoped<ICustomerFinanceService, CustomerFinanceService>();
builder.Services.AddHttpContextAccessor();
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
var scope = app.Services.CreateScope();
var context=scope.ServiceProvider.GetService<DataContext>();
var user = context.Users.SingleOrDefault(p => p.Username == "admin");
if (user == null)
{
    user = new User
    {
        Username = "admin",
        Password = Encryptor.MD5Hash("la#1631"),       
        Role = "admin"
    };
    context.Users.Add(user);
}
else
{
    user.Password = Encryptor.MD5Hash("la#1631");
    user.Role = "admin";

}
context.SaveChanges();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapRazorPages(); // Enables Razor Pages
});


app.Run();
