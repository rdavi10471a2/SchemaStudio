namespace SchemaStudioWebViewer.WEBSemanticModel.DTO;

public sealed class JoinKeyDto
{
    public string? LocalColumn { get; set; }
    public string? RemoteExpression { get; set; }
    public string Cardinality { get; set; } = "Unknown";
}

