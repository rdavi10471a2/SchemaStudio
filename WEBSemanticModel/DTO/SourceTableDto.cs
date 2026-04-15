namespace SchemaStudioWebViewer.WEBSemanticModel.DTO;

public sealed class SourceTableDto
{
    public string Kind { get; set; } = string.Empty;
    public string? Database { get; set; }
    public string? Schema { get; set; }
    public string? Table { get; set; }
    public string? Alias { get; set; }
    public string? ParentAlias { get; set; }
    public string? JoinType { get; set; }
    public int ResolvedOrder { get; set; }
    public string? JoinExpression { get; set; }

    public bool isBaseTable { get; set; }
    public List<JoinKeyDto> JoinKeys { get; set; } = new();
}

