using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

public sealed class SourceViewDefinition
{
    [Display(Name = "Database", Order = 10)]
    [Description("Database containing the available source view.")]
    public string DatabaseName { get; set; } = "";

    [Display(Name = "Schema", Order = 20)]
    [Description("Schema containing the available source view.")]
    public string SchemaName { get; set; } = "";

    [Display(Name = "Object", Order = 30)]
    [Description("SQL Server view name available for import.")]
    public string ObjectName { get; set; } = "";

    [Display(Name = "Modified", Order = 40)]
    [Description("Most recent SQL Server modify timestamp for the source view.")]
    public DateTime? ModifyDate { get; set; }

    [Display(Name = "Full Name", Order = 50)]
    [Description("Fully qualified source view name.")]
    public string FullName => string.Join(".",
        new[] { DatabaseName, SchemaName, ObjectName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
