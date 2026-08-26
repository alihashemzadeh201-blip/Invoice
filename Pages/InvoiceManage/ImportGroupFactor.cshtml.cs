using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DocumentFormat.OpenXml;
using OfficeOpenXml.Core.ExcelPackage;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
[Authorize]
public class ImportGroupFactorModel : PageModel
{
    private readonly IImportTemplateService _templateService;
    private readonly IInvoiceService _invoiceService;

    public ImportGroupFactorModel(IImportTemplateService templateService, IInvoiceService invoiceService)
    {
        _templateService = templateService;
        _invoiceService = invoiceService;
    }

    public int CurrentStep { get; set; } = 1;
    public List<string> ExcelColumns { get; set; }
    public List<ImportTemplate> Templates { get; set; }
    public int? SelectedTemplateId { get; set; }
    public Dictionary<string, string> CurrentMapping { get; set; }
    public List<InvoicePreviewData> PreviewData { get; set; }

    public async Task OnGetAsync(int step = 1)
    {
        CurrentStep = step;

        // Load templates for current profile
        Templates = await _templateService.GetTemplatesForCurrentProfile();

        // Restore session data
        if (TempData["ExcelColumns"] != null)
        {
            ExcelColumns = System.Text.Json.JsonSerializer.Deserialize<List<string>>(TempData["ExcelColumns"].ToString());
            TempData.Keep("ExcelColumns");
        }

        if (TempData["SelectedTemplateId"] != null)
        {
            SelectedTemplateId = int.Parse(TempData["SelectedTemplateId"].ToString());
            TempData.Keep("SelectedTemplateId");

            // Load mapping
            CurrentMapping = await _templateService.GetMapping(SelectedTemplateId.Value);
        }

        if (TempData["PreviewData"] != null)
        {
            PreviewData = System.Text.Json.JsonSerializer.Deserialize<List<InvoicePreviewData>>(TempData["PreviewData"].ToString());
            TempData.Keep("PreviewData");
        }
    }

    public async Task<IActionResult> OnPostUploadFileAsync(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            ModelState.AddModelError("", "لطفاً فایل اکسل را انتخاب کنید");
            return Page();
        }

        // Set EPPlus License

        try
        {
            var path = $"Uploads/{Path.GetRandomFileName()}.xlsx";
            using (var fs = new FileStream(path, FileMode.Create))
            {
                await excelFile.CopyToAsync(fs);
            }
            var doc = ExcelHelp.Open(path);
            // Save to TempData
            TempData["ExcelColumns"] = System.Text.Json.JsonSerializer.Serialize(doc.ColumnHeaders);
            TempData["ExcelFilePath"] = path;

            return RedirectToPage(new { step = 2 });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"خطا در خواندن فایل: {ex.Message}");
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSelectTemplateAsync(int? templateId)
    {
        if (templateId.HasValue)
        {
            TempData["SelectedTemplateId"] = templateId.Value;

            // Load mapping from template
            var mapping = await _templateService.GetMapping(templateId.Value);
            TempData["CurrentMapping"] = System.Text.Json.JsonSerializer.Serialize(mapping);

            return RedirectToPage(new { step = 3 });
        }

        return RedirectToPage(new { step = 2 });
    }

    public async Task<IActionResult> OnPostCreateTemplateAsync(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            ModelState.AddModelError("", "نام تمپلیت نمی‌تواند خالی باشد");
            return Page();
        }

        var template = await _templateService.CreateTemplate(templateName);
        TempData["SelectedTemplateId"] = template.Id;

        return RedirectToPage(new { step = 3 });
    }

    public async Task<IActionResult> OnPostSaveMappingAsync(int templateId, Dictionary<string, string> mapping)
    {
        // Save mapping to template
        await _templateService.SaveMapping(templateId, mapping);

        TempData["CurrentMapping"] = System.Text.Json.JsonSerializer.Serialize(mapping);

        // Generate preview data
        var filePath = TempData["ExcelFilePath"]?.ToString();
        if (!string.IsNullOrEmpty(filePath))
        {
            var previewData = await GeneratePreviewData(filePath, mapping);
            TempData["PreviewData"] = System.Text.Json.JsonSerializer.Serialize(previewData);
            TempData.Keep("ExcelFilePath");
        }

        return RedirectToPage(new { step = 4 });
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        var previewDataJson = TempData["PreviewData"]?.ToString();
        if (string.IsNullOrEmpty(previewDataJson))
        {
            return RedirectToPage(new { step = 1 });
        }

        var previewData = System.Text.Json.JsonSerializer.Deserialize<List<InvoicePreviewData>>(previewDataJson);

        // Filter valid records
        var validRecords = previewData.Where(x => x.IsValid).ToList();

        // Import to database
        var result = await _invoiceService.ImportInvoices(validRecords);
        if (result.Count == 0)
            TempData["SuccessMessage"] = $" فاکتور با موفقیت ایمپورت شد. <br/> تعداد سطرهای بارگذاری شده : {validRecords.Count}";
        if (result.Count > 0)
        {
            TempData["WarningMessage"] = $" فاکتور به دلیل خطا ایمپورت نشد.<br/> {string.Join("<br/>",result)}";
        }

        // Clear temp data
        TempData.Remove("ExcelColumns");
        TempData.Remove("ExcelFilePath");
        TempData.Remove("PreviewData");
        TempData.Remove("CurrentMapping");

        return RedirectToPage("ImportGroupFactorConfirm");
    }

    private async Task<string> SaveExcelFileTemporary(IFormFile file)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        using var stream = new FileStream(tempPath, FileMode.Create);
        await file.CopyToAsync(stream);
        return tempPath;
    }

    private async Task<List<InvoicePreviewData>> GeneratePreviewData(string filePath, Dictionary<string, string> mapping)
    {
        var previewData = new List<InvoicePreviewData>();
        
        var doc = ExcelHelp.Open(filePath);

        //ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        //using var package = new ExcelPackage(new FileInfo(filePath));
        //var worksheet = package.Workbook.Worksheets[0];

        //// Get column indices from mapping
        var columnIndices = new Dictionary<string, int>();
        for (int col = 0; col < doc.ColumnHeaders.Count; col++)
        {
            var columnName = doc.ColumnHeaders[col];
            if (!string.IsNullOrEmpty(columnName))
            {
                var fieldName = mapping.FirstOrDefault(x => x.Value == columnName).Key;
                if (!string.IsNullOrEmpty(fieldName))
                {
                    columnIndices[fieldName] = col;
                }
            }
        }

        // Read data rows
        for (int row = 0; row < doc.Rows.Count; row++)
        {
            var data = new InvoicePreviewData();

            try
            {
                data.InvoiceCode = GetCellValue(doc, row, columnIndices, "InvoiceCode");
                data.InvoiceDate = GetCellValue(doc, row, columnIndices, "InvoiceDate");
                data.InvoiceType =Enum.Parse<InvoiceType>( GetCellValue(doc, row, columnIndices, "InvoiceType"));
                data.InvoicePattern =Enum.Parse<InvoicePattern>( GetCellValue(doc, row, columnIndices, "InvoicePattern"));
                data.PaymentMethod =Enum.Parse<CheckoutType>( GetCellValue(doc, row, columnIndices, "PaymentMethod"));
                data.PersonType =Enum.Parse<PersonType>( GetCellValue(doc, row, columnIndices, "PersonType"));
                data.BuyerEconomicCode = GetCellValue(doc, row, columnIndices, "BuyerEconomicCode");
                data.ProductCode = GetCellValue(doc, row, columnIndices, "ProductCode");
                data.ProductDescription = GetCellValue(doc, row, columnIndices, "ProductDescription");

                var quantityStr = GetCellValue(doc, row, columnIndices, "Quantity");
                data.Quantity = string.IsNullOrEmpty(quantityStr) ? null : decimal.Parse(quantityStr);

                var priceStr = GetCellValue(doc, row, columnIndices, "UnitPrice");
                data.UnitPrice = string.IsNullOrEmpty(priceStr) ? null : decimal.Parse(priceStr);

                var discountStr = GetCellValue(doc, row, columnIndices, "Discount");
                data.Discount = string.IsNullOrEmpty(discountStr) ? null : decimal.Parse(discountStr);

                var taxStr = GetCellValue(doc, row, columnIndices, "TaxPercent");
                data.TaxPercent = string.IsNullOrEmpty(taxStr) ? null : decimal.Parse(taxStr);

                var unit = GetCellValue(doc, row, columnIndices, "Unit");
                data.Unit = unit;
                // Validate
                data.Validate();
            }
            catch (Exception ex)
            {
                data.IsValid = false;
                data.ValidationErrors = ex.Message;
            }

            previewData.Add(data);
        }

        return previewData;
    }

    private string GetCellValue(ExcelDoc doc, int row, Dictionary<string, int> columnIndices, string fieldName)
    {
        if (columnIndices.ContainsKey(fieldName))
        {
            return doc.Rows[row].Cells[columnIndices[fieldName]];
        }
        return "";
    }
}

// Models



public class ImportResult
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}