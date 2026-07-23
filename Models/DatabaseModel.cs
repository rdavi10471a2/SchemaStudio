using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.Models
{
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

        [Display(Name = "View Name Filter", Order = 60)]
        [Description("Optional pattern or expression used to include only matching source view names for this database.")]
        public string? ViewNameFilter { get; set; }

        [Display(AutoGenerateField = false)]
        public bool Active { get; set; }
    }
}
