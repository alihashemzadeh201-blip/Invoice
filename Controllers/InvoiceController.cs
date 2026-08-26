using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Invoice.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoiceController : Controller
    {
        DataContext _context;
        public InvoiceController(DataContext context)
        {
            _context = context;
        }


        [HttpGet]
        public IActionResult GetInvoices(string? taxid, string? buyerTin, InvoiceStatus? status)
        {
            var invoices = _context.Invoices.Include(p => p.InvoiceItems);
            if (taxid != null)
                invoices = invoices.Where(p => p.Taxid.Contains(taxid)).Include(p => p.InvoiceItems);
            if (buyerTin != null)
                invoices = invoices.Where(p => p.Tinb != null && p.Tinb.Contains(buyerTin)).Include(p => p.InvoiceItems);
            if(status != null)
                invoices=invoices.Where(p=>p.Status==status).Include(p => p.InvoiceItems);
            var list = invoices.ToList();
            return Ok(list);
        }
    }
}
