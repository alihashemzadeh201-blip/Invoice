namespace Invoice.Models
{
    public class ImportTemplateMapping
    {
        public int Id { get; set; }
        public int TemplateId { get; set; }
        public string FieldName { get; set; }
        public string ExcelColumnName { get; set; }

        // Navigation
        public ImportTemplate Template { get; set; }
    }
}
