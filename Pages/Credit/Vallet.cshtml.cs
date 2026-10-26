using Invoice.Code;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Invoices.Pages.Vallet
{
    [Authorize]
    public class ValletModel : PageModel
    {
        private readonly DataContext _context;
        private readonly IBaleNotificationService _baleService;

        public int Vallet { get; set; }
        [BindProperty]
        public Transaction Transaction { get; set; }=new Transaction();

        public ValletModel(DataContext context, IBaleNotificationService baleService)
        {
            _context = context;
            _baleService = baleService;
        }
        public async Task<IActionResult> OnGetAsync()
        {
            var user=await _context.Users.FindAsync(User.Identity.Name);
            Vallet = user.Vallet;
            Transaction.CustomerUsername = User.Identity.Name;
            return Page();
        }
      
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }
            Transaction.CustomerUsername = User.Identity.Name;
            Transaction.Date=DateTime.Now;  

            _context.Transactions.Add(Transaction);
            await _context.SaveChangesAsync();              

            await _baleService.SendWalletRequestNotificationAsync(Transaction);

            return RedirectToPage("ValletTransactions", new { Message = "درخواست افزایش موجودی شما با موفقیت ثبت شد. پس تایید توسط اوپراتور ، موجودی کیف پول شما افزایش خواهد یافت." });
        }
    }
}
