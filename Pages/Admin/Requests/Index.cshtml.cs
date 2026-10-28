using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Invoice.Code;

namespace Invoice.Pages_Admin_KarpooshehRequest
{
    [Authorize(Roles = "admin")]
    public class IndexModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;
        private readonly IBaleUserBotService? _baleUserBotService;

        public IndexModel(
            Invoices.Models.DataContext context,
            IBaleUserBotService? baleUserBotService = null)
        {
            _context = context;
            _baleUserBotService = baleUserBotService;
        }

        public IList<KarpooshehRequest> KarpooshehRequest { get; set; } = default!;

        public async Task OnGetAsync()
        {
            if (_context.KarpooshehRequests != null)
            {
                KarpooshehRequest = await _context.KarpooshehRequests
                    .Where(p => p.FinishDate == null)
                    .OrderByDescending(p => p.RequestDate)
                    .ToListAsync();
            }
            else
            {
                KarpooshehRequest = new List<KarpooshehRequest>();
            }
        }

        public async Task<IActionResult> OnGetReceiveAsync(int id)
        {
            var request = await _context.KarpooshehRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            request.ReceiveDate = DateTime.Now;
            await _context.SaveChangesAsync();

            // اطلاع‌رسانی به متقاضی در پیام‌رسان بله
            if (_baleUserBotService != null && !string.IsNullOrWhiteSpace(request.BaleChatId))
            {
                try
                {
                    var msg = $"✅ متقاضی گرامی <b>{request.Name}</b>،\n\n" +
                              $"درخواست شما با شماره پرونده <b>#{request.Id}</b> توسط کارشناس دریافت گردید و در نوبت استعلام و صدور شناسه یکتای مالیاتی قرار گرفت.\n" +
                              $"به محض تکمیل مراحل، اطلاعات در همین ربات برای شما ارسال خواهد شد.";
                    await _baleUserBotService.SendUserMessageAsync(request.BaleChatId, msg);
                }
                catch { }
            }

            return RedirectToPage("Index");
        }

        public async Task<IActionResult> OnGetRejectAsync(int id)
        {
            return await OnGetDeleteAsync(id);
        }

        public async Task<IActionResult> OnGetDeleteAsync(int id)
        {
            var request = await _context.KarpooshehRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            // اطلاع‌رسانی رد درخواست به متقاضی در بله
            if (_baleUserBotService != null && !string.IsNullOrWhiteSpace(request.BaleChatId))
            {
                try
                {
                    var msg = $"❌ متقاضی گرامی <b>{request.Name}</b>،\n\n" +
                              $"درخواست اخذ شناسه یکتا (پرونده <b>#{request.Id}</b>) رد گردید.";
                    await _baleUserBotService.SendUserMessageAsync(request.BaleChatId, msg);
                }
                catch { }
            }

            _context.KarpooshehRequests.Remove(request);
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }
}
