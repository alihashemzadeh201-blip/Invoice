using Invoice.Models;

public class ImportTemplate
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int ProfileId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Profile Profile { get; set; }
    public ICollection<ImportTemplateMapping> Mappings { get; set; }
}
