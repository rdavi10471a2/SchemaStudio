namespace RelationshipLoader;

// Copied from SchemaStudioWebViewer.Data.TableDisplayColumnPolicy so the utility picks the SAME
// default lookup display column the Base View Creator does. Attributes stripped; logic identical.
// Preferences are database-scoped; per-table entries override the generic fallback, and the chosen
// column must still exist on the referenced table.
public sealed class TableDisplayColumnPolicy
{
    private static readonly IReadOnlyList<string> GenericPreferenceOrder =
    [
        "Des",
        "Des1",
        "Name",
        "Description",
        "Desc",
        "Title",
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> DatabasePreferences =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ExcedeSchema"] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["dbo.COEMP"] = ["Name"],
                ["dbo.COLOOKUP"] = ["Des1", "Des"],
                ["dbo.COTAX"] = ["Des"],
                ["dbo.COTRM"] = ["Des"],
                ["dbo.COBRN"] = ["Name", "Des"],
                ["dbo.COCUS"] = ["Name"],
                ["dbo.COVEN"] = ["Name"],
            },
        };

    public IReadOnlyList<string> GetPreferredDisplayColumns(string databaseName, string schemaName, string tableName)
    {
        var tableKey = $"{schemaName}.{tableName}";
        if (DatabasePreferences.TryGetValue(databaseName, out var tablePreferences) &&
            tablePreferences.TryGetValue(tableKey, out var preferredColumns))
        {
            return preferredColumns
                .Concat(GenericPreferenceOrder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return GenericPreferenceOrder;
    }
}
