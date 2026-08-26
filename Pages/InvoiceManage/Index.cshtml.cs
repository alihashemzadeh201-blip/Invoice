using DocumentFormat.OpenXml.Wordprocessing;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.ComponentModel.DataAnnotations;

namespace Invoices.Pages.InvoiceManage
{
        [Authorize]
    public class IndexModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;
        private readonly ISelectedProfile _profile;
        public List<Invoices.Models.Invoice> InvoiceList { get; set; }
       
        [BindProperty]
        public int? Status { get; set; }
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 20;

        [BindProperty(SupportsGet = true)]
        public string DateRange { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Start { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? End { get; set; }
        [BindProperty]
        public string? BuyerId { get; set; }
        public IndexModel(Invoices.Models.DataContext context,ISelectedProfile profile)
        {
            _context = context;
            _profile = profile;
        }
        public async Task<IActionResult> OnGetAsync()
        {
           
            //ProfileId=ProfileId ?? HttpContext.Session.GetInt32("profile");
            //if(ProfileId!=null)
            //    HttpContext.Session.SetInt32("profile", ProfileId.Value);

            Status =  HttpContext.Session.GetInt32("status")==-1?null: HttpContext.Session.GetInt32("status");


            //ProfileId = ProfileId ?? _sharedInfo?.CurrentProfileId;
            //_sharedInfo.CurrentProfileId = ProfileId;
            await loadData();
            return Page();
        }
        public async Task<IActionResult> OnGetLoadMoreAsync(int? pageNumber,int? status,string? start,string? end,string? buyerId)
        {
            PageNumber = pageNumber??1;
            Status = status;
            BuyerId= buyerId;
            Start= start;
            End= end;
            await loadData();
            return Partial("_InvoiceRows",InvoiceList);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            HttpContext.Session.SetInt32("status", Status??-1);

            await loadData();
            return Page();
        }

        private async Task loadData()
        {
            var sDate=MyDate.GetEnglishDate(Start);
            var eDate = MyDate.GetEnglishDate(End);            
            var invoices = _context.Invoices.Where(p => p.Sender == User.Identity.Name && (Start==null || p.SendDate>=sDate) && (End==null || p.SendDate<eDate.Value.AddDays(1)) && p.Tinb.Contains(BuyerId??"") && p.Draft==false && (p.Status!= InvoiceStatus.موفق ||  p.Ins!= InvoiceTopic.ابطالی)).Include(p => p.InvoiceItems);

            if (Status != null)
                invoices = invoices.Where(p => p.Status == (InvoiceStatus?)Status).Include(p => p.InvoiceItems);
            if (User.IsInRole("reseller"))
            {
                //var profiles = await _context.Profiles.Where(p => p.Creator == User.Identity.Name).ToListAsync();
                //if (ProfileId == null)
                //    ProfileId = profiles.FirstOrDefault()?.Id;
                //ViewData["ProfileId"] = new SelectList(profiles, "Id", "DisplayName", ProfileId);
                invoices = invoices.Where(p => p.ProfileId == _profile.ProfileId).Include(p => p.InvoiceItems);
            }
            //else if (User.IsInRole("کاربر"))
            //{
            //    var user = await _context.Users.SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
            //    ProfileId = user.ProfileId;
            //}
            InvoiceList = await invoices.OrderByDescending(p=>p.Id).Skip((PageNumber-1)*PageSize).Take(PageSize).ToListAsync();
            ViewData["Status"] = new SelectList(EnumUtils.GetEnumSelectList<InvoiceStatus?>(), "Value", "Text", Status);
        }
        public async Task<IActionResult> OnGetExportExcelAsync()
        {
            //ProfileId = ProfileId ?? HttpContext.Session.GetInt32("profile");
            //if (ProfileId != null)
            //    HttpContext.Session.SetInt32("profile", ProfileId.Value);

            //ProfileId = ProfileId ?? _sharedInfo?.CurrentProfileId;
            //_sharedInfo.CurrentProfileId = ProfileId;
            var invoices = _context.Invoices.Where(p => p.Sender == User.Identity.Name).Include(p => p.InvoiceItems);
            if (User.IsInRole("reseller"))
            {
                //var profiles = await _context.Profiles.Where(p => p.Creator == User.Identity.Name).ToListAsync();
                //if (ProfileId == null)
                //    ProfileId = profiles.FirstOrDefault()?.Id;
                invoices = invoices.Where(p => p.ProfileId == _profile.ProfileId).Include(p => p.InvoiceItems);
            }          
            InvoiceList = await invoices.ToListAsync();
            ExcelDoc excel = new ExcelDoc();
            excel.Title = "لیست فاکتور ها";
            excel.ColumnHeaders = new List<string> { "ردیف", "شماره منحصر به فرد مالیاتی", "نوع صورتحساب", "مبلغ کل", "مجموع مالیات ارزش افزوده", "مجموع تخفیف", "روش تسویه",  "تاریخ صدور فاکتور", "تاریخ ارسال فاکتور", "الگوی فاکتور", "موضوع فاکتور", "شماره فاکتور مرجع", "شماره اقتصادی خریدار","وضعیت" };
            excel.Rows = new List<ExcelRow>();
            int index = 1;
            foreach (var item in InvoiceList)
            {
                excel.Rows.Add(new ExcelRow
                {
                    Cells = new List<string> { (index++).ToString(), item.Taxid, item.Inty.ToString(), item.Tadis.ToString(), item.Tvam.ToString(), item.Tdis.ToString(), item.Setm.ToString(), item.InvoiceDate, MyDate.GetPersianDate(item.SendDate).ToString(), item.Inp.ToString(), item.Ins.ToString(), item.Irtaxid, item.Tinb,item.Status.ToString() }
                });
            }
            var filename ="wwwroot/files/"+ Path.GetRandomFileName() + ".xlsx";
            ExcelHelp.Save(excel,filename);
            return File(filename.Replace("wwwroot",""), "application/vnd.ms-excel", "لیست فاکتور.xlsx");
        }
    }
}
