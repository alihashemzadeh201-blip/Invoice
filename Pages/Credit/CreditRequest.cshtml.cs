using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.Credit
{
    [Authorize(Roles ="user")]
    public class CreditRequestModel : PageModel
    {
        DataContext _context;
        public List<CreditPeriod> Periods { get; set; }
        [BindProperty]
        public CreditRequest CreditRequest { get; set; }
        public CreditRequestModel(DataContext context)
        {
            _context = context;

        }
        public bool NoMoney { get; set; }
        public DateTime? ExpireDate { get; set; }
        public async Task OnGetAsync()
        {
            Periods = await _context.CreditPeriods.ToListAsync();
            var user = await _context.Users.FindAsync(User.Identity.Name);
            ExpireDate = user.ExpireDate;
        }
        public async Task<IActionResult> OnPostAsync()
        {
            CreditRequest.Username = User.Identity.Name;
            CreditRequest.Date = DateTime.Now;
                var user = await _context.Users.FindAsync(User.Identity.Name);
            if(CreditRequest.PeriodId== 0)
            {
                ModelState.AddModelError("", "لطفا دوره مورد نظر را انتخاب نمایید");
                Periods = await _context.CreditPeriods.ToListAsync();
                ExpireDate = user.ExpireDate;
                return Page();
            }
            var period = await _context.CreditPeriods.FindAsync(CreditRequest.PeriodId);

            if (user.Vallet < period.Price)
            {
                Periods = await _context.CreditPeriods.ToListAsync();
                NoMoney = true;
                ExpireDate = user.ExpireDate;
                return Page();
            }
            CreditRequest.Price = period.Price;
            CreditRequest.Months = period.Months;
            _context.Add(CreditRequest);
            user.Vallet -= period.Price;
            user.ExpireDate = (user.ExpireDate ?? DateTime.Now).AddMonths(period.Months);

            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }
}
