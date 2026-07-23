using Microsoft.SqlServer.TransactSql.ScriptDom;


namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    public class SelectItem
    {
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

        //-----------------------------------------
        // SEMANTIC SOURCE (RESOLVED)
        //-----------------------------------------
        public string SemanticDatabase { get; set; }

        public string SemanticSchema { get; set; }

        public string SemanticObject { get; set; }

        public string SemanticColumn { get; set; }


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
