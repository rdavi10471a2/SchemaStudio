using Microsoft.SqlServer.TransactSql.ScriptDom;

using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    [FileVersion("1.0")]
    [AIFileContext("WEBSemanticModel/Model/SelectItem.cs", "Carries parser-resolved select-item identity, lineage, and parser-owned metadata while the SQL query is being analyzed.", Responsibilities = "Carries DisableInheritance so comment-bound semantic lookup overrides can survive the trip from parser binding into projected parsed columns.", Nuances = "Keep this type narrowly focused on parser-captured state and avoid letting user-owned metadata become parser-owned by accident.", RelatedFiles = "ViewMetadataBinder, ParsedQuery, ViewSourcedColumnDefinition", LastReviewed = "2026-04-25")]
    [AIChange("1.0", "2026-04-25 12:18 PM CDT added DisableInheritance to the parser select-item model so comment-bound semantic override tags survive into the parsed column projection.", AICommandStatus.Pending)]
    public class SelectItem
    {
        // 2026-04-25 12:18 PM CDT AI v1.0 marker: parsed select items now carry DisableInheritance from SQL metadata comments.
        //-----------------------------------------
        // OUTPUT
        //-----------------------------------------
        public string Database { get; set; }

        public string Alias { get; set; }

        public string Expression { get; set; }

        public ColumnKind Kind { get; set; }

        //-----------------------------------------
        // AST HANDLE
        //-----------------------------------------
        internal ScalarExpression ExpressionNode { get; set; }

        public string ExpressionText { get; set; }

      //  public bool AllowIherit { get; set; }

        //-----------------------------------------
        // SOURCE (DIRECT BINDING)
        //-----------------------------------------
        internal ColumnBinding Binding { get; set; } = new();

        //-----------------------------------------
        // BASE LINEAGE (RESOLVED)
        //-----------------------------------------
        public string BaseDatabase { get; set; }

        public string BaseSchema { get; set; }

        public string BaseTable { get; set; }

        public string BaseColumn { get; set; }


        public string ExpressionDatabase { get; set; }

        public string ExpressionSchema { get; set; }

        public string ExpressionTable { get; set; }

        public string ExpressionColumn { get; set; }

        public int ResolvedOrder { get; set; }


        public string Comment { get; set; }
        public string BusinessName { get; set; }
        public string BusinessDescription { get; set; }
        public string DeveloperNotes { get; set; }
        public bool DisableInheritance { get; set; }
        //-----------------------------------------
        // DISPLAY
        //-----------------------------------------
        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Alias))
                return $"{Expression} AS {Alias}";

            return Expression;
        }
    }
}
