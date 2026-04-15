namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    public class ParsedQuery
    {
        public string SourceQuery { get; set; }

        public List<SelectItem> SelectItems { get; set; } = new();

        public List<SourceTable> SourceTables { get; set; } = new();

        //-----------------------------------------
        // 🔥 FINAL PROJECTED COLUMNS (UI / METADATA)
        //-----------------------------------------
        public List<ViewSourcedColumnDefinition> Columns { get; set; } = new();


        public List<string> ValidateColumnSemantics(string contextTable)
        {
            var errors = new List<string>();

            //-----------------------------------------
            // RULE A: Semantic + Lineage
            //-----------------------------------------
            foreach (var col in Columns)
            {
                if (string.IsNullOrWhiteSpace(col.ColumnName))
                    errors.Add("Column missing name");

                if (col.ColumnKind == ColumnKind.Simple)
                {
                    if (string.IsNullOrWhiteSpace(col.BaseTable))
                        errors.Add($"{col.ColumnName} lost lineage");

                    if (col.BaseTable == contextTable)
                        errors.Add($"{col.ColumnName} incorrectly resolves to view");

                    if (string.IsNullOrWhiteSpace(col.BaseColumn))
                        errors.Add($"{col.ColumnName} missing base column");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(col.BaseTable) &&
                        !col.BaseTable.Equals(contextTable, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"{col.ColumnName} expression leaked lineage ({col.BaseTable})");
                    }
                }
            }

            //-----------------------------------------
            // RULE B: Coverage (your addition)
            //-----------------------------------------
            var usedTables = Columns
                .Where(c => c.ColumnKind == ColumnKind.Simple)
                .Select(c => c.BaseTable)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var declaredTables = SourceTables
                .Select(t => t.Table)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var t in usedTables)
            {
                if (!declaredTables.Contains(t))
                    errors.Add($"Lineage references table not in source graph: {t}");
            }

            //-----------------------------------------
            // OPTIONAL: sanity
            //-----------------------------------------
            if (!declaredTables.Any())
                errors.Add("No source tables discovered");

            return errors;
        }

        //-----------------------------------------
        // 🔥 PROJECTION STEP
        //-----------------------------------------
        public List<ViewSourcedColumnDefinition> ToColumns(
            string contextDatabase,
            string contextSchema,
            string contextTable)
        {
            var result = new List<ViewSourcedColumnDefinition>();

            for (int i = 0; i < SelectItems.Count; i++)
            {
                var item = SelectItems[i];

                var col = new ViewSourcedColumnDefinition
                {
                    //-----------------------------------------
                    // ORDER
                    //-----------------------------------------
                    OrdinalPosition = i + 1,

                    //-----------------------------------------
                    // OUTPUT NAME (The Alias)
                    // 🔥 In s.CustomerID AS Customer_ID, this is "Customer_ID"
                    //-----------------------------------------
                    ColumnName = item.Alias,

                    //-----------------------------------------
                    // 🔥 PROJECTION OWNER (VIEW)
                    //-----------------------------------------
                    Database = contextDatabase,
                    Schema = contextSchema,
                    Table = contextTable,

                    //-----------------------------------------
                    // 🔥 BASE LINEAGE
                    // 🔥 In s.CustomerID AS Customer_ID, BaseColumn is "CustomerID"
                    //-----------------------------------------
                    BaseDatabase = item.BaseDatabase,
                    BaseSchema = item.BaseSchema,
                    BaseTable = item.BaseTable,
                    BaseColumn = item.BaseColumn,
                    

                    Comment = item.Comment,
                    BusinessName = item.BusinessName,
                    BusinessDescription = item.BusinessDescription,
                    DeveloperNotes = item.DeveloperNotes,
                   // CanInheritBase = item.AllowIherit,
                    ColumnKind = item.Kind
                };

                result.Add(col);
            }

            return result;
        }
    }
}
