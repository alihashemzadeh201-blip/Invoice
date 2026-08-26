using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Invoice.Pages.InvoiceManage
{
    public class ImportInvoiceItemsModel : PageModel
    {
        DataContext _context;
        public ImportInvoiceItemsModel(DataContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet =true)]
        public int InvoiceId { get; set; }

        [BindProperty]
        [Required(ErrorMessage =Messages.Required)]
        public IFormFile File { get; set; }
        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var path = $"Uploads/{Path.GetRandomFileName()}.xlsx";
            using(var fs=new FileStream(path, FileMode.Create))
            {
                await File.CopyToAsync(fs);
            }
            var doc=ExcelHelp.Open(path);
            int index = 0;
            foreach (var item in doc.Rows)
            {
                //if (index==0)
                //{
                //    index++;
                //    continue;
                //}
                index++;
                if (item.Cells.Count == 0 || item.Cells[0] == "" )
                    break;
                try
                {
                    var mustr = item.Cells[6].Replace(" ", "");

                    var mu = await _context.MeasureUnits.FirstOrDefaultAsync(p => p.Title.StartsWith(mustr));
                    if(mu==null)
                    {
                        ModelState.AddModelError("", $"واحد سنجش در سطر {index} نامعتبر است");
                        continue;
                    }
                    var invoiceItem = new InvoiceItem
                    {
                        Sstid = item.Cells[0],
                        Sstt = item.Cells[1],
                        Am = decimal.Parse(item.Cells[2]),
                        Fee = decimal.Parse(item.Cells[3]),
                        Dis = decimal.Parse(item.Cells[4]),
                        Vra = (VatRate)int.Parse(item.Cells[5]),
                        Mu = mu.Code,
                        InvoiceId=InvoiceId
                    };
                    _context.Add(invoiceItem);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"اطلاعات سطر {index} فایل اکسل اشکال دارد لطفا بررسی نمایید.");
                }
            }
            if(ModelState.ErrorCount> 0)
            {
                return Page();
            }
            else
            {
                await _context.SaveChangesAsync();
                return RedirectToPage("InvoiceItems", new { invoiceid = InvoiceId });
            }

        }
    }
}
