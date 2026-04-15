namespace SchemaStudioWebViewer.WEBSemanticModel.DTO;

public sealed class ViewColumnDto
{
    public int ColumnId { get; set; }
    public int TableId { get; set; }
    public int OrdinalPosition { get; set; }
   
    public string? Database { get; set; }
    public string? Schema { get; set; }
    public string? Table { get; set; }
    public string? ColumnName { get; set; }

    public string ColumnKind { get; set; } = string.Empty;
    public string? BaseDatabase { get; set; }
    public string? BaseSchema { get; set; }
    public string? BaseTable { get; set; }
    public string? BaseColumn { get; set; }


 /// <summary>
 /// /  public bool CanInheritBase { get; set; }
 /// </summary>


    public string? BusinessName { get; set; }
    public string? BusinessDescription { get; set; }
    public string? DeveloperNotes { get; set; }
    public string? Comment { get; set; }
    public DateTime LastSynced { get; set; }
    public bool IsDirty { get; set; }
}

