using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.Account
{
    public class ExpiredModel : PageModel
    {
        DataContext _context;
        public List<CreditPeriod> Periods { get; set; }
        [BindProperty]
        public CreditRequest CreditRequest { get; set; }
        public ExpiredModel(DataContext context)
        {
            _context = context;

        }

        public async Task OnGetAsync()
        {
            Periods = await _context.CreditPeriods.ToListAsync();
        }
        public async Task<IActionResult> OnPostAsync()
        {
            CreditRequest.Username = User.Identity.Name;
            CreditRequest.Date = DateTime.Now;
            var period = await _context.CreditPeriods.FindAsync(CreditRequest.PeriodId);
            CreditRequest.Price = period.Price;
            CreditRequest.Months = period.Months;
            _context.Add(CreditRequest);
            await _context.SaveChangesAsync();
            return RedirectToPage("SetCardNo",new {requestid=CreditRequest.Id});
        }
    }
}
