using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Invoice.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class InvoiceController : Controller
    {
        private const int MaxPageSize = 200;

        DataContext _context;
        private readonly ILogger<InvoiceController> _logger;

        public InvoiceController(DataContext context, ILogger<InvoiceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// دریافت صورتحساب‌های کاربر جاری.
        /// فقط فاکتورهایی که توسط همین کاربر ارسال شده‌اند برگردانده می‌شود و
        /// تعداد نتایج با صفحه‌بندی محدود شده است.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetInvoices(string? taxid, string? buyerTin, InvoiceStatus? status, int pageNumber = 1, int pageSize = 50)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            if (pageNumber < 1)
                pageNumber = 1;
            if (pageSize < 1)
                pageSize = 1;
            if (pageSize > MaxPageSize)
                pageSize = MaxPageSize;

            var invoices = _context.Invoices
                .Include(p => p.InvoiceItems)
                .Where(p => p.Sender == username);

            if (taxid != null)
                invoices = invoices.Where(p => p.Taxid != null && p.Taxid.Contains(taxid));
            if (buyerTin != null)
                invoices = invoices.Where(p => p.Tinb != null && p.Tinb.Contains(buyerTin));
            if (status != null)
                invoices = invoices.Where(p => p.Status == status);

            var list = await invoices
                .OrderByDescending(p => p.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            _logger.LogInformation("دریافت {Count} صورتحساب از طریق API توسط کاربر {Username}", list.Count, username);

            return Ok(list);
        }
    }
}
