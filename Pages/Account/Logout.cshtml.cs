using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Invoices.Pages.Account
{
    public class LogoutModel : PageModel
    {
        [BindProperty(SupportsGet =true)]
        public string ReturnUrl { get; set; }
        public void OnGet()
        {

        }
        public ActionResult OnPost()
        {
            HttpContext.SignOutAsync();
            return LocalRedirect(ReturnUrl);

        }
    }
}
