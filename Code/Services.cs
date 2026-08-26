using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.EntityFrameworkCore;


public interface IImportTemplateService
{
    Task<List<ImportTemplate>> GetTemplatesForCurrentProfile();
    Task<ImportTemplate> CreateTemplate(string name);
    Task<Dictionary<string, string>> GetMapping(int templateId);
    Task SaveMapping(int templateId, Dictionary<string, string> mapping);
    Task<bool> DeleteTemplate(int templateId);
}

public interface IInvoiceService
{
    Task<List<string>> ImportInvoices(List<InvoicePreviewData> data);
}

// ============= Implementations =============

public class ImportTemplateService : IImportTemplateService
{
    private readonly DataContext _context;
    private readonly ISelectedProfile _selectedProfile;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ImportTemplateService(
        DataContext context,
        ISelectedProfile selectedProfile,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _selectedProfile = selectedProfile;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<List<ImportTemplate>> GetTemplatesForCurrentProfile()
    {
        var currentProfile = _selectedProfile.Profile;
        if (currentProfile == null)
            return new List<ImportTemplate>();

        return await _context.ImportTemplates
            .Where(t => t.ProfileId == currentProfile.Id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<ImportTemplate> CreateTemplate(string name)
    {
        var currentProfile = _selectedProfile.Profile;
        if (currentProfile == null)
            throw new Exception("پروفایل انتخاب نشده است");

        var template = new ImportTemplate
        {
            Name = name,
            ProfileId = currentProfile.Id,
            CreatedAt = DateTime.Now
        };

        _context.ImportTemplates.Add(template);
        await _context.SaveChangesAsync();

        return template;
    }

    public async Task<Dictionary<string, string>> GetMapping(int templateId)
    {
        var mappings = await _context.ImportTemplateMappings
            .Where(m => m.TemplateId == templateId)
            .ToListAsync();

        return mappings.ToDictionary(m => m.FieldName, m => m.ExcelColumnName);
    }

    public async Task SaveMapping(int templateId, Dictionary<string, string> mapping)
    {
        // حذف مپینگ‌های قبلی
        var existingMappings = await _context.ImportTemplateMappings
            .Where(m => m.TemplateId == templateId)
            .ToListAsync();

        _context.ImportTemplateMappings.RemoveRange(existingMappings);

        // اضافه کردن مپینگ‌های جدید
        foreach (var item in mapping)
        {
            if (!string.IsNullOrEmpty(item.Value))
            {
                _context.ImportTemplateMappings.Add(new ImportTemplateMapping
                {
                    TemplateId = templateId,
                    FieldName = item.Key,
                    ExcelColumnName = item.Value
                });
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteTemplate(int templateId)
    {
        var template = await _context.ImportTemplates.FindAsync(templateId);
        if (template == null)
            return false;

        // حذف مپینگ‌ها
        var mappings = await _context.ImportTemplateMappings
            .Where(m => m.TemplateId == templateId)
            .ToListAsync();
        _context.ImportTemplateMappings.RemoveRange(mappings);

        // حذف تمپلیت
        _context.ImportTemplates.Remove(template);
        await _context.SaveChangesAsync();

        return true;
    }
}

public class InvoiceService : IInvoiceService
{
    private readonly DataContext _context;
    private readonly ISelectedProfile _selectedProfile;
    private readonly HttpContext _httpContext;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        DataContext context,
        ISelectedProfile selectedProfile,
        IHttpContextAccessor httpContext,
        ILogger<InvoiceService> logger)
    {
        _context = context;
        _selectedProfile = selectedProfile;
        _logger = logger;
        _httpContext = httpContext.HttpContext;
    }

    public async Task<List<string>> ImportInvoices(List<InvoicePreviewData> data)
    {
        var result = new List<string>();
        var _profile = _selectedProfile.Profile;

        if (_profile == null)
        {
            result.Add("ابتدا پروفایل فعال را انتخاب نمایید");
            return result;
        }

        //var doc = ExcelHelp.Open(path);
        int index = 0;
        var groups = data.GroupBy(p => new { Code = p.InvoiceCode, Date = p.InvoiceDate, Type = p.InvoiceType, Pattern = p.InvoicePattern, CheckoutType = p.PaymentMethod, BuyerType = p.PersonType, BuyerTin = p.BuyerEconomicCode });
        //var user = _context.Users.Include(p => p.Profile).SingleOrDefault(p => p.Username == User.Identity.Name);
        foreach (var group in groups)
        {
            try
            {
                var Invoice = new Invoices.Models.Invoice
                {
                    Sender = _httpContext.User.Identity.Name,
                    Ins = InvoiceTopic.اصلی,
                    Inty =group.Key.Type,
                    Inp = group.Key.Pattern,
                    InvoiceDate = group.Key.Date,
                    Tob = group.Key.BuyerType,
                    Tinb = group.Key.BuyerTin,
                    Setm = group.Key.CheckoutType,
                    Draft = true,
                };
                //if (User.IsInRole("user"))
                //{
                Invoice.Tins = _profile.TinCode;
                Invoice.ProfileId = _selectedProfile.ProfileId;
                //}
                //else if (User.IsInRole("reseller"))
                //{
                //    //var profile = await _context.Profiles.FindAsync(_profile.ProfileId);
                //    Invoice.Tins = _profile.Profile.TinCode;
                //    Invoice.ProfileId = _profile.ProfileId;
                //}

                foreach (var item in group)
                {

                    index++;
                    //if (item.Cells.Count == 0 || item.Cells[0] == "")
                    //    break;

                    var mustr = item.Unit.Replace(" ", "");

                    var mu = await _context.MeasureUnits.FirstOrDefaultAsync(p => p.Title.Contains(mustr));
                    if (mu == null)
                    {
                        result.Add($"واحد سنجش در سطر {index} نامعتبر است");
                        continue;
                    }
                    var invoiceItem = new InvoiceItem
                    {
                        Sstid = item.ProductCode,
                        Sstt = item.ProductDescription,
                        Am = item.Quantity,
                        Fee = item.UnitPrice,
                        Dis = item.Discount,
                        Vra = (VatRate)item.TaxPercent,
                        Mu = mu.Code,

                    };
                    Invoice.InvoiceItems.Add(invoiceItem);

                }
                _context.Invoices.Add(Invoice);
            }
            catch (Exception ex)
            {
                result.Add($"اطلاعات سطر {index} فایل اکسل اشکال دارد لطفا بررسی نمایید.");
            }
        }
        if (result.Count == 0)
            _context.SaveChanges();
        return result;
    }

    private DateTime? ParsePersianDate(string persianDate)
    {
        if (string.IsNullOrEmpty(persianDate))
            return null;

        try
        {
            // فرض: تاریخ به فرمت 1403/09/15
            var parts = persianDate.Split('/');
            if (parts.Length == 3)
            {
                var year = int.Parse(parts[0]);
                var month = int.Parse(parts[1]);
                var day = int.Parse(parts[2]);

                var persianCalendar = new System.Globalization.PersianCalendar();
                return persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not parse Persian date: {persianDate}. Error: {ex.Message}");
        }

        return null;
    }

    private decimal CalculateTotalPrice(InvoicePreviewData item)
    {
        var quantity = item.Quantity ?? 0;
        var unitPrice = item.UnitPrice ?? 0;
        var discount = item.Discount ?? 0;
        var taxPercent = item.TaxPercent ?? 9;

        var subtotal = quantity * unitPrice;
        var afterDiscount = subtotal - discount;
        var tax = afterDiscount * (taxPercent / 100);
        var total = afterDiscount + tax;

        return total;
    }
}

public class InvoicePreviewData
{
    public string InvoiceCode { get; set; }
    public string InvoiceDate { get; set; }
    public InvoiceType InvoiceType { get; set; }
    public InvoicePattern InvoicePattern { get; set; }
    public CheckoutType PaymentMethod { get; set; }
    public PersonType PersonType { get; set; }
    public string BuyerEconomicCode { get; set; }
    public string ProductCode { get; set; }
    public string ProductDescription { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Discount { get; set; }
    public decimal? TaxPercent { get; set; }
    public string Unit { get; set; }

    public bool IsValid { get; set; } = true;
    public string ValidationErrors { get; set; }

    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(InvoiceCode))
            errors.Add("کد فاکتور الزامی است");

        if (string.IsNullOrEmpty(ProductCode))
            errors.Add("شناسه کالا الزامی است");

        if (!Quantity.HasValue || Quantity <= 0)
            errors.Add("تعداد باید بیشتر از صفر باشد");

        if (!UnitPrice.HasValue || UnitPrice <= 0)
            errors.Add("مبلغ واحد باید بیشتر از صفر باشد");

        if (errors.Any())
        {
            IsValid = false;
            ValidationErrors = string.Join(", ", errors);
        }
    }
}