using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Invoices.Pages.Shared
{
    [AllowAnonymous]
    public class _FormLayoutModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
