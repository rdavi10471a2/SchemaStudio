
namespace SchemaStudioWebViewer.Data;

public sealed class TableDisplayColumnPolicy
{
    private static readonly IReadOnlyList<string> GenericPreferenceOrder =
    [
        "Des",
        "Des1",
        "Name",
        "Description",
        "Desc",
        "Title"
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
            }
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

    public IReadOnlyList<string> GetCandidateColumnNames(string databaseName)
    {
        var databaseColumns = DatabasePreferences.TryGetValue(databaseName, out var tablePreferences)
            ? tablePreferences.Values.SelectMany(columns => columns)
            : Enumerable.Empty<string>();

        return databaseColumns
            .Concat(GenericPreferenceOrder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
