using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.WEBSemanticModel.DTO;

public sealed class ViewColumnDto
{
    [Display(AutoGenerateField = false)]
    public int ColumnId { get; set; }

    [Display(AutoGenerateField = false)]
    public int TableId { get; set; }

    [Display(Name = "Sequence", Order = 10)]
    [Description("The ordinal position of the field within the parsed view definition.")]
    public int OrdinalPosition { get; set; }

    [Display(AutoGenerateField = false)]
    public string? Database { get; set; }

    [Display(AutoGenerateField = false)]
    public string? Schema { get; set; }

    [Display(AutoGenerateField = false)]
    public string? Table { get; set; }

    [Display(Name = "View Column Name", Order = 20)]
    [Description("The column name projected by the parsed view.")]
    public string? ColumnName { get; set; }

    [Display(Name = "Column Kind", Order = 25)]
    [Description("The semantic classification of the column, such as simple, aggregate, or expression.")]
    public string ColumnKind { get; set; } = string.Empty;

    [Display(
        Name = "Base Database",
        Description = "Underlying source database for the resolved source column.",
        AutoGenerateField = false)]
    public string? BaseDatabase { get; set; }

    [Display(
        Name = "Base Schema",
        Description = "Underlying source schema for the resolved source column.",
        AutoGenerateField = false)]
    public string? BaseSchema { get; set; }

    [Display(
        Name = "Base Object",
        Description = "Underlying source table or view for the resolved source column.",
        AutoGenerateField = false)]
    public string? BaseTable { get; set; }

    [Display(
        Name = "Base Column",
        Description = "Underlying source column for the resolved source column.",
        AutoGenerateField = false)]
    public string? BaseColumn { get; set; }

    [Display(
        Name = "Fully Qualified Source",
        Description = "The fully qualified physical source for this column when lineage is available.",
        AutoGenerateField = true,
        Order = 11)]
    public string FullyQualifiedSourceColumnName =>
        SqlQualify(BaseDatabase, BaseSchema, BaseTable, BaseColumn);

    [Display(Name = "Business Name", Order = 30)]
    [Description("Business Name for this column.")]
    public string? BusinessName { get; set; }

    [Display(Name = "Description", Order = 40)]
    [Description("Overview of the purpose of this column and what it represents")]
    [DataType(DataType.MultilineText)]
    public string? BusinessDescription { get; set; }

    [Display(Name = "Dev Notes", Order = 50)]
    [Description("Additional Notes and information about this column and details on advanced use")]
    [DataType(DataType.MultilineText)]
    public string? DeveloperNotes { get; set; }

    [Display(Name = "Comment", Order = 60)]
    [Description("Comment text captured from the parsed SQL definition for this column.")]
    [DataType(DataType.MultilineText)]
    public string? Comment { get; set; }

    [Display(AutoGenerateField = false)]
    public DateTime LastSynced { get; set; }

    [Display(AutoGenerateField = false)]
    public bool IsDirty { get; set; }

    private static string SqlQualify(params string?[] parts) =>
        string.Join(".",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => $"[{part}]"));
}
