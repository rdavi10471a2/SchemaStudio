using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

public sealed class DatabaseDefinition
{
    [Display(AutoGenerateField = false)]
    public int DatabaseId { get; set; }

    [Required]
    [StringLength(128)]
    [Display(Name = "Database Name", Order = 10)]
    [Description("Physical SQL Server database name.")]
    public string DatabaseName { get; set; } = "";

    [Required]
    [StringLength(128)]
    [Display(Name = "Default Schema", Order = 20)]
    [Description("Default schema used when object metadata does not specify one.")]
    public string DefaultSchema { get; set; } = "dbo";

    [Required]
    [StringLength(128)]
    [Display(Name = "Business Name", Order = 30)]
    [Description("Business-facing name for this database.")]
    public string BusinessName { get; set; } = "";

    [Required]
    [StringLength(500)]
    [Display(Name = "Business Description", Order = 40)]
    [Description("Business summary for this database.")]
    public string BusinessDescription { get; set; } = "";

    [StringLength(4000)]
    [Display(Name = "Developer Notes", Order = 50)]
    [Description("Technical notes for maintainers and developers.")]
    public string? DeveloperNotes { get; set; }

    [Display(Name = "Active", Order = 60)]
    public bool Active { get; set; } = true;
}
