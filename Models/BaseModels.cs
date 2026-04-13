using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.Models
{
    [AttributeUsage(AttributeTargets.Property)]
    public class MultilineDisplayRequiredAttribute : Attribute
    {
        public bool IsMultiline { get; }
        public int InitialHeight { get; }

        public MultilineDisplayRequiredAttribute(bool isMultiline, int height = 100)
        {
            IsMultiline = isMultiline;
            InitialHeight = height;
        }
    }
    [AttributeUsage(AttributeTargets.Property)]
    public class DetailViewOnlyAttribute : Attribute
    {
        public bool IsDetailOnly { get; }

        public DetailViewOnlyAttribute(bool isDetailOnly = true)
        {
            IsDetailOnly = isDetailOnly;
        }
    }

    public class DatabaseModel
    {
        [Display(AutoGenerateField = false)]
        public int DatabaseId { get; set; }

        [Display(Name = "Database Name", Order = 10)]
        [Description("The physical SQL Server database name.")]
        public string DatabaseName { get; set; } = "";

        [Display(AutoGenerateField = false)]
        [Description("Default Schema for the Database")]
        public string DefaultSchema { get; set; } = "";

        [Display(Name = "Business Alias", Order = 30)]
        [Description("Common Name for the Database in the Enterprise.")]
        public string? BusinessName { get; set; }

        [Display(Name = "Summary", Order = 40)]
        [Description("A high-level overview of what this data source represents within the Enterprise.")]
        [MultilineDisplayRequired(true, 150)]
        public string? BusinessDescription { get; set; }

        [Display(Name = "Technical Notes", Order = 50)]
        [Description("Additional Notes")]
        [MultilineDisplayRequired(true, 250)]
        public string? DeveloperNotes { get; set; }

        [Display(AutoGenerateField = false)]
        public bool Active { get; set; }
    }

    public class DatabaseDomainModel
    {
        [Display(AutoGenerateField = false)]
        public int DatabaseDomainId { get; set; }

        [Display(AutoGenerateField = false)]
        public int DatabaseId { get; set; }

        [Display(Name = "Business Domain", Order = 10)]
        [Description("The functional area (e.g., General Ledger, AP, Payroll).")]
        public string Domain { get; set; } = "";
    }

    public class SchemaObjectModel
    {
        [Display(AutoGenerateField = false)]
        public int SchemaObjectId { get; set; }

        [Display(AutoGenerateField = false)]
        public int DatabaseId { get; set; }

        [Display(AutoGenerateField = false)]
        public string? SourceDatabaseName { get; set; }

        [Display(AutoGenerateField = false)]
        public string SourceSchemaName { get; set; } = "";

        [Display(AutoGenerateField = false)]
        public string SourceObjectName { get; set; } = "";

        [Display(Name = "Physical Source", Order = 5)]
        [Description("The fully qualified 3-part name of the source object.")]
        [DetailViewOnly]
        public string SourceName => $"{SourceDatabaseName}.{SourceSchemaName}.{SourceObjectName}";

        [Display(Name = "Business Name", Order = 30)]
        [Description("Name for this View Object that represents is Business Meaning.")]
        public string? BusinessName { get; set; }

        [Display(Name = "Description", Order = 40)]
        [Description("Overview of the purpose of this view and what it represents")]
        [MultilineDisplayRequired(true, 150)]
        public string? BusinessDescription { get; set; }

        [Display(Name = "Dev Notes", Order = 50)]
        [Description("Additional Notes and information about this view and details on advanced use")]
        [MultilineDisplayRequired(true, 250)]
        public string? DeveloperNotes { get; set; }

        [Display(Name = "Base Object", Order = 60)]
        [Description("Indicates if this view represents a root business concept")]
        public bool IsBaseObject { get; set; }

        [Display(Name = "Domain", Order = 70)]
        [Description("The functional area (e.g., General Ledger, AP, Payroll) of the business that the data for this view belongs to")]
        public string? Domain { get; set; }

        [Display(AutoGenerateField = false)]
        public bool IsActive { get; set; }

        [Display(AutoGenerateField = false)]
        public DateTime LastSynced { get; set; }
    }

    public class SchemaObjectColumnModel
    {
        [Display(AutoGenerateField = false)]
        public int SchemaObjectColumnId { get; set; }

        [Display(AutoGenerateField = false)]
        public int SchemaObjectId { get; set; }


        [Display(
            Name = "Base Database",
            Description = "Underlying source database for the base schema object.",
            AutoGenerateField = false
        )]
        public string BaseDatabaseName { get; set; } = "";

        [Display(
            Name = "Base Schema",
            Description = "Underlying source schema for the base schema object.",
            AutoGenerateField = false
        )]
        public string BaseSchemaName { get; set; } = "";

        [Display(
            Name = "Base Object",
            Description = "Underlying source table/object for this view column.",
            AutoGenerateField = false
        )]
        public string BaseObjectName { get; set; } = "";

        [Display(
    Name = "Base Column",
    Description = "Underlying source Column.",
    AutoGenerateField = false
)]
        public string BaseColumnName { get; set; } = "";


        [Display(
            Name = "Fully Qualified Source",
            Description = "Fully qualified Phyisical Source for this column.",
            AutoGenerateField = true, Order = 11
        )]
        [DetailViewOnly]
        public string FullyQualifiedSourceColumnName =>
    SqlQualify(
        BaseDatabaseName,
        BaseSchemaName,
        BaseObjectName,
        BaseColumnName
    );

        [Display(Name = "Sequence", Order = 10)]
        [Description("The ordinal position of the field within the view definition.")]
        public int OrdinalPosition { get; set; }

        [Display(Name = "View Column Name", Order = 20)]
        [Description("The Column Name used in the view.")]
        public string SourceColumnName { get; set; } = "";

        [Display(Name = "Business Name", Order = 30)]
        [Description("Business Name for this column.")]
        public string? BusinessName { get; set; }

        [Display(Name = "Description", Order = 40)]
        [Description("Overview of the purpose of this column and what it represents")]
        [MultilineDisplayRequired(true, 150)]
        public string? BusinessDescription { get; set; }

        [Display(Name = "Dev Notes", Order = 50)]
        [Description("Additional Notes and information about this column and details on advanced use")]
        [MultilineDisplayRequired(true, 450)]
        public string? DeveloperNotes { get; set; }

        [Display(AutoGenerateField = false)]
        public bool IsBaseDefinition { get; set; }

        [Display(AutoGenerateField = false)]
        public bool? DisableInheritance { get; set; }

        [Display(AutoGenerateField = false)]
        public DateTime LastSynced { get; set; }

        public static string SqlQualify(params string[] parts) =>
    string.Join(".",
        parts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => $"[{x}]"));
    }

    [Display(AutoGenerateField = false)]
    public class DisplaySchemaObject
    {
        public int? SchemaObjectId { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsHeader { get; set; }
        public bool IsBaseObject { get; set; }
    }
}