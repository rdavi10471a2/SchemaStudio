using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.WEBSemanticModel.DTO;

public sealed class SourceTableDto
{
    [Display(Name = "Source Kind", Order = 10)]
    [Description("The semantic source type, such as base table, view, or derived source.")]
    public string Kind { get; set; } = string.Empty;

    [Display(Name = "Database", Order = 20)]
    [Description("The database that owns the referenced source object.")]
    public string? Database { get; set; }

    [Display(Name = "Schema", Order = 30)]
    [Description("The schema that owns the referenced source object.")]
    public string? Schema { get; set; }

    [Display(Name = "Object Name", Order = 40)]
    [Description("The table or view name used by the parsed source.")]
    public string? Table { get; set; }

    [Display(Name = "Alias", Order = 50)]
    [Description("The SQL alias used for this source within the parsed query.")]
    public string? Alias { get; set; }

    [Display(Name = "Parent Alias", Order = 60)]
    [Description("The parent alias when this source is nested beneath another source.")]
    public string? ParentAlias { get; set; }

    [Display(Name = "Join Type", Order = 70)]
    [Description("The join type used to connect this source to the rest of the query.")]
    public string? JoinType { get; set; }

    [Display(Name = "Resolved Order", Order = 80)]
    [Description("The order in which the parser resolved this source.")]
    public int ResolvedOrder { get; set; }

    [Display(Name = "Join Expression", Order = 90)]
    [Description("The full join expression associated with this source, when one exists.")]
    [DataType(DataType.MultilineText)]
    public string? JoinExpression { get; set; }

    [Display(Name = "Base Table", Order = 100)]
    [Description("Indicates whether this source resolved to a base table rather than another derived query.")]
    public bool IsBaseTable { get; set; }

    [Display(Name = "Physical Source", Order = 110)]
    [Description("The fully qualified source object name when database, schema, and table values are available.")]
    public string PhysicalSourceName =>
        string.Join(".",
            new[] { Database, Schema, Table }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

    [Display(AutoGenerateField = false)]
    public List<JoinKeyDto> JoinKeys { get; set; } = new();
}
