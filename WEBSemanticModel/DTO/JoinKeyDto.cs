using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.WEBSemanticModel.DTO;

public sealed class JoinKeyDto
{
    [Display(Name = "Local Column", Order = 10)]
    [Description("The local column participating in the join relationship.")]
    public string? LocalColumn { get; set; }

    [Display(Name = "Remote Expression", Order = 20)]
    [Description("The remote column or expression matched by the local column.")]
    public string? RemoteExpression { get; set; }

    [Display(Name = "Cardinality", Order = 30)]
    [Description("The inferred join cardinality for this key relationship.")]
    public string Cardinality { get; set; } = "Unknown";
}
