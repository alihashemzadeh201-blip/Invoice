using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Invoice.Pages.Request
{
    public class SentSuccessModel : PageModel
    {
        [BindProperty(SupportsGet =true)]
        public int RequestId { get; set; }
        public void OnGet()
        {
        }
    }
}
