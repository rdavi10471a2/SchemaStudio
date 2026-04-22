using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text;

namespace SchemaStudio.Data.Models;

public sealed class SchemaObjectColumnDefinition
{
    private int _schemaObjectColumnId;
    private int _schemaObjectId;
    private int _ordinalPosition;
    private string _sourceColumnName = "";
    private string? _sourceColumnKind;
    private string? _baseDatabaseName;
    private string? _baseSchemaName;
    private string? _baseObjectName;
    private string? _baseColumnName;
    private bool _isBaseDefinition;
    private bool _disableInheritance;
    private string? _businessName;
    private string? _businessDescription;
    private string? _developerNotes;
    private DateTime _lastSynced = DateTime.Now;
    private bool _isDirty;

    [Display(AutoGenerateField = false)]
    public int SchemaObjectColumnId
    {
        get => _schemaObjectColumnId;
        set => SetField(ref _schemaObjectColumnId, value);
    }

    [Required]
    [Range(1, int.MaxValue)]
    [Display(AutoGenerateField = false)]
    public int SchemaObjectId
    {
        get => _schemaObjectId;
        set => SetField(ref _schemaObjectId, value);
    }

    [Required]
    [Display(Name = "Order", Order = 10)]
    public int OrdinalPosition
    {
        get => _ordinalPosition;
        set => SetField(ref _ordinalPosition, value);
    }

    [Required]
    [StringLength(256)]
    [Display(Name = "Source Column", Order = 20)]
    [Description("Column name from the parsed source object.")]
    public string SourceColumnName
    {
        get => _sourceColumnName;
        set => SetField(ref _sourceColumnName, value);
    }

    [StringLength(64)]
    [Display(Name = "Source Column Kind", Order = 30)]
    [Description("Parsed column classification.")]
    public string? SourceColumnKind
    {
        get => _sourceColumnKind;
        set => SetField(ref _sourceColumnKind, value);
    }

    [StringLength(128)]
    [Display(Name = "Base Database", Order = 40)]
    public string? BaseDatabaseName
    {
        get => _baseDatabaseName;
        set => SetField(ref _baseDatabaseName, value);
    }

    [StringLength(128)]
    [Display(Name = "Base Schema", Order = 50)]
    public string? BaseSchemaName
    {
        get => _baseSchemaName;
        set => SetField(ref _baseSchemaName, value);
    }

    [StringLength(256)]
    [Display(Name = "Base Object", Order = 60)]
    public string? BaseObjectName
    {
        get => _baseObjectName;
        set => SetField(ref _baseObjectName, value);
    }

    [StringLength(256)]
    [Display(Name = "Base Column", Order = 70)]
    public string? BaseColumnName
    {
        get => _baseColumnName;
        set => SetField(ref _baseColumnName, value);
    }

    [Display(Name = "Base Definition", Order = 80)]
    public bool IsBaseDefinition
    {
        get => _isBaseDefinition;
        set => SetField(ref _isBaseDefinition, value);
    }

    [Display(Name = "Disable Inheritance", Order = 90)]
    public bool DisableInheritance
    {
        get => _disableInheritance;
        set => SetField(ref _disableInheritance, value);
    }

    [StringLength(128)]
    [Display(Name = "Business Name", Order = 100)]
    public string? BusinessName
    {
        get => _businessName;
        set => SetField(ref _businessName, value);
    }

    [StringLength(500)]
    [Display(Name = "Business Description", Order = 110)]
    public string? BusinessDescription
    {
        get => _businessDescription;
        set => SetField(ref _businessDescription, value);
    }

    [StringLength(3200)]
    [Display(Name = "Developer Notes", Order = 120)]
    public string? DeveloperNotes
    {
        get => _developerNotes;
        set => SetField(ref _developerNotes, value);
    }

    [Display(Name = "Last Synced", AutoGenerateField = false)]
    public DateTime LastSynced
    {
        get => _lastSynced;
        set => SetField(ref _lastSynced, value);
    }

    [Display(AutoGenerateField = false)]
    public bool IsDirty
    {
        get => _isDirty;
        set => _isDirty = value;
    }

    public void ClearDirty() => IsDirty = false;

    public string FormatDescription()
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine($"English Name: {BusinessName}");
        builder.AppendLine($"English Description: {BusinessDescription}");
        builder.AppendLine($"Developer Notes: {DeveloperNotes}");
        return builder.ToString();
    }

    public override string ToString() => string.IsNullOrWhiteSpace(BusinessName) ? SourceColumnName : BusinessName;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;

        if (propertyName != nameof(IsDirty))
        {
            IsDirty = true;
        }
    }
}
