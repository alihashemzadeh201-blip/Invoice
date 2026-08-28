using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

/// <summary>
/// ایجاد و مدیریت نشست (Sign-in) کاربر با استفاده از احراز هویت کوکی.
/// نکته: هش و بررسی رمز عبور در <see cref="PasswordHashManager"/> انجام می‌شود.
/// </summary>
public class Identity
{
    public static async Task SignIn(HttpContext context, string username, string role)
    {
        var claims = new List<Claim>
                {
                  new Claim(ClaimTypes.Name, username),
                  new Claim(ClaimTypes.Role,role)
                };

        var claimsIdentity = new ClaimsIdentity(
          claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties();
        authProperties.IsPersistent = true;
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
           new ClaimsPrincipal(claimsIdentity),
           authProperties);
    }
}
