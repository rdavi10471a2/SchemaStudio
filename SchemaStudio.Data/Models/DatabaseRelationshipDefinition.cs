using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudio.Data.Models;

public sealed class DatabaseRelationshipDefinition
{
    [Display(AutoGenerateField = false)]
    public int DatabaseRelationshipId { get; set; }

    [Display(AutoGenerateField = false)]
    public int DatabaseId { get; set; }

    [Required]
    [StringLength(128)]
    [Display(Name = "Source Schema", Order = 10)]
    [Description("Schema for the table on the source side of the relationship.")]
    public string SourceSchemaName { get; set; } = "dbo";

    [Required]
    [StringLength(128)]
    [Display(Name = "Source Table", Order = 20)]
    [Description("Table on the source side of the relationship.")]
    public string SourceTableName { get; set; } = "";

    [Required]
    [StringLength(128)]
    [Display(Name = "Target Schema", Order = 30)]
    [Description("Schema for the table on the target side of the relationship.")]
    public string TargetSchemaName { get; set; } = "dbo";

    [Required]
    [StringLength(128)]
    [Display(Name = "Target Table", Order = 40)]
    [Description("Table on the target side of the relationship.")]
    public string TargetTableName { get; set; } = "";

    [Required]
    [StringLength(32)]
    [Display(Name = "Join Type", Order = 50)]
    [Description("Default join keyword used when this relationship is projected into generated SQL.")]
    public string JoinType { get; set; } = "LEFT JOIN";

    [Required]
    [StringLength(2000)]
    [Display(Name = "Join Expression", Order = 55)]
    [Description("The ON-clause expression that joins the source table to the target table.")]
    public string JoinExpression { get; set; } = "";

    [Required]
    [StringLength(32)]
    [Display(Name = "Discovery Source", Order = 80)]
    [Description("Where this relationship came from, such as Manual, SchemaLookup, or SchemaChild.")]
    public string DiscoverySource { get; set; } = "Manual";

    [StringLength(128)]
    [Display(Name = "Source Constraint", Order = 90)]
    [Description("Optional source-system constraint name used to refresh detected relationships.")]
    public string? SourceConstraintName { get; set; }

    [Display(Name = "Include Lookup By Default", Order = 110)]
    [Description("Base View Creator should include this lookup by default.")]
    public bool IncludeLookupByDefault { get; set; }

    [StringLength(128)]
    [Display(Name = "Display Column", Order = 140)]
    [Description("Optional target display column used when this relationship behaves like a lookup.")]
    public string? DisplayColumnName { get; set; }

    [StringLength(128)]
    [Display(Name = "Filter Column", Order = 150)]
    [Description("Optional target filter column used for constrained lookup relationships.")]
    public string? FilterColumnName { get; set; }

    [StringLength(128)]
    [Display(Name = "Filter Value", Order = 160)]
    [Description("Optional target filter value used with Filter Column.")]
    public string? FilterValue { get; set; }

    [StringLength(1000)]
    [Display(Name = "Developer Notes", Order = 180)]
    [Description("Technical notes about why this relationship exists or how it should be used.")]
    public string? DeveloperNotes { get; set; }

    [Display(AutoGenerateField = false)]
    public DateTime CreatedOn { get; set; }

    [Display(AutoGenerateField = false)]
    public DateTime UpdatedOn { get; set; }

    public List<DatabaseRelationshipColumnDefinition> Columns { get; set; } = new();

    public string SourceObject => $"{SourceSchemaName}.{SourceTableName}";

    public string TargetObject => $"{TargetSchemaName}.{TargetTableName}";

    public string ColumnSummary => !string.IsNullOrWhiteSpace(JoinExpression)
        ? JoinExpression
        : Columns.Count == 0
        ? ""
        : string.Join(
            " AND ",
            Columns
                .OrderBy(column => column.OrdinalPosition)
                .Select(column => $"{SourceTableName}.{column.SourceColumnName} = {TargetTableName}.{column.TargetColumnName}"));
}

public sealed class DatabaseRelationshipColumnDefinition
{
    [Display(AutoGenerateField = false)]
    public int DatabaseRelationshipColumnId { get; set; }

    [Display(AutoGenerateField = false)]
    public int DatabaseRelationshipId { get; set; }

    [Display(Name = "Ordinal", Order = 10)]
    public int OrdinalPosition { get; set; } = 1;

    [Required]
    [StringLength(128)]
    [Display(Name = "Source Column", Order = 20)]
    public string SourceColumnName { get; set; } = "";

    [Required]
    [StringLength(128)]
    [Display(Name = "Target Column", Order = 30)]
    public string TargetColumnName { get; set; } = "";
}
