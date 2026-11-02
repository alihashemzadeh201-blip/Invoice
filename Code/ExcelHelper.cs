using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Linq;
using DocumentFormat.OpenXml;
using System.Data;

public class ExcelHelp
{
    public static ExcelDoc Open(string path)
    {
        using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Open(path, false))
        {
            return ParseSpreadsheetDocument(spreadsheetDocument);
        }
    }

    public static ExcelDoc Open(Stream stream)
    {
        using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Open(stream, false))
        {
            return ParseSpreadsheetDocument(spreadsheetDocument);
        }
    }

    private static ExcelDoc ParseSpreadsheetDocument(SpreadsheetDocument spreadsheetDocument)
    {
        ExcelDoc doc = new ExcelDoc();

        WorkbookPart workbookPart = spreadsheetDocument.WorkbookPart;
        IEnumerable<Sheet> sheets = workbookPart.Workbook.GetFirstChild<Sheets>().Elements<Sheet>();
        string relationshipId = sheets.First().Id.Value;
        WorksheetPart worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationshipId);
        Worksheet worksheet = worksheetPart.Worksheet;
        SheetData sheetData = worksheet.GetFirstChild<SheetData>();
        var firstRow = true;
        foreach (Row row in sheetData.Elements<Row>())
        {
            ExcelRow tempRow = new ExcelRow();
            int currentColIndex = 0;
            foreach (Cell cell in row.Elements<Cell>())
            {
                int cellColIndex = GetColumnIndex(cell.CellReference?.Value);
                if (cellColIndex >= 0)
                {
                    // پر کردن سلول‌های خالی جا افتاده قبل از سلول فعلی
                    while (currentColIndex < cellColIndex)
                    {
                        tempRow.Cells.Add(string.Empty);
                        currentColIndex++;
                    }
                    tempRow.Cells.Add(GetCellValue(cell, workbookPart));
                    currentColIndex = cellColIndex + 1;
                }
                else
                {
                    tempRow.Cells.Add(GetCellValue(cell, workbookPart));
                    currentColIndex++;
                }
            }
            if (firstRow)
            {
                firstRow = false;
                doc.ColumnHeaders.AddRange(tempRow.Cells);
            }
            else
            {
                // تکمیل تعداد سلول‌های ردیف تا انتهای ستون‌ها در صورت خالی بودن سلول‌های پایانی
                while (doc.ColumnHeaders != null && tempRow.Cells.Count < doc.ColumnHeaders.Count)
                {
                    tempRow.Cells.Add(string.Empty);
                }
                doc.Rows.Add(tempRow);
            }
        }

        return doc;
    }

    public static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference)) return -1;
        int col = 0;
        for (int i = 0; i < cellReference.Length; i++)
        {
            char c = cellReference[i];
            if (char.IsLetter(c))
            {
                col = col * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            }
            else
            {
                break;
            }
        }
        return col > 0 ? col - 1 : -1;
    }

    private static string GetCellValue(Cell cell, WorkbookPart workbookPart)
    {
        string value = cell.InnerText;

        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
        {
            if (int.TryParse(value, out int sstIndex) && workbookPart.SharedStringTablePart?.SharedStringTable != null)
            {
                var element = workbookPart.SharedStringTablePart.SharedStringTable.ChildElements.ElementAtOrDefault(sstIndex);
                return element?.InnerText ?? string.Empty;
            }
            return value;
        }
        else
        {
            return value ?? string.Empty;
        }
    }
    public static void Save(ExcelDoc doc, string path)
    {
        createSpreadsheetWorkbook(path);
        using (SpreadsheetDocument sd = SpreadsheetDocument.Open(path, true))
        {
            WorkbookPart part = sd.WorkbookPart;
            WorksheetPart spart = part.WorksheetParts.First();
            SheetData sheet = spart.Worksheet.GetFirstChild<SheetData>();
            Row row = new Row();
            var rIndex = 1;
            var cIndex = 0;
            if (doc.Title != null)
            {
                var cell = new Cell();
                var address = "A1";
                cell.CellReference = address;
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(doc.Title));
                row.AppendChild(cell);
                sheet.AppendChild(row);
                rIndex++;
            }
            if (doc.ColumnHeaders != null)
            {
                row = new Row();
                foreach (var item in doc.ColumnHeaders)
                {
                    var cell = new Cell();
                    var address = columnName(cIndex++) + rIndex;
                    cell.CellReference = address;
                    cell.DataType = CellValues.InlineString;
                    cell.InlineString = new InlineString(new Text(item));
                    row.AppendChild(cell);
                }
                sheet.AppendChild(row);

                rIndex++;
            }
            foreach (var item in doc.Rows)
            {
                cIndex = 0;
                row = new Row();
                foreach (var c in item.Cells)
                {
                    var cell = new Cell();
                    var address = columnName(cIndex++) + rIndex;
                    cell.CellReference = address;
                    cell.DataType = CellValues.InlineString;
                    cell.InlineString = new InlineString(new Text(c));
                    row.AppendChild(cell);
                }
                sheet.AppendChild(row);
                rIndex++;
            }
            spart.Worksheet.Save();
            part.Workbook.Save();

        }

    }




    private static void createSpreadsheetWorkbook(string filepath)
    {
        // Create a spreadsheet document by supplying the filepath.
        // By default, AutoSave = true, Editable = true, and Type = xlsx.
        SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.
            Create(filepath, SpreadsheetDocumentType.Workbook);

        // Add a WorkbookPart to the document.
        WorkbookPart workbookpart = spreadsheetDocument.AddWorkbookPart();
        workbookpart.Workbook = new Workbook();

        // Add a WorksheetPart to the WorkbookPart.
        WorksheetPart worksheetPart = workbookpart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData());

        // Add Sheets to the Workbook.
        Sheets sheets = spreadsheetDocument.WorkbookPart.Workbook.
            AppendChild<Sheets>(new Sheets());

        // Append a new worksheet and associate it with the workbook.
        Sheet sheet = new Sheet()
        {
            Id = spreadsheetDocument.WorkbookPart.
            GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "mySheet"
        };
        sheets.Append(sheet);

        workbookpart.Workbook.Save();

        // Close the document.
        spreadsheetDocument.Dispose();
    }
    private static string columnName(int index)
    {
        var col = index % 26;
        var pre = "";
        if (index > 26)
            pre = "A";

        return pre + Convert.ToChar(col + 65).ToString();
    }
}
public class ExcelDoc
{
    public List<string> ColumnHeaders { get; set; }
    public List<ExcelRow> Rows { get; set; }
    public string Title { get; set; }
    public ExcelDoc()
    {
        ColumnHeaders = null;
        Rows = new List<ExcelRow>();
        ColumnHeaders = new List<string>();
    }
}

public class ExcelRow
{
    public List<string> Cells { get; set; }

    public ExcelRow()
    {
        Cells = new List<string>();
    }
}
