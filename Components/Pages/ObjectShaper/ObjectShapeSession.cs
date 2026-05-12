using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Components.Pages.ObjectShaper;

[FileVersion("1.0")]
[AIFileContext(
    "Components/Pages/ObjectShaper/ObjectShapeSession.cs",
    "Selection and dependency session for the Object Shaper page.",
    Responsibilities = "Own parsed SQL state, selected projection fields, join dependency evaluation, generated SQL invalidation, and render-ready rows.",
    Nuances = "The session treats joins as consequences of selected fields and WHERE predicates; source FK fields do not force lookup joins unless a lookup/display field is selected.")]
public sealed class ObjectShapeSession
{
    private readonly ObjectShapeSqlParser parser = new();
    private readonly HashSet<string> selectedFieldKeys = new(StringComparer.OrdinalIgnoreCase);
    private string generatedSql = string.Empty;
    private bool generatedSqlDirty = true;

    public ObjectShapeParseResult? ParseResult { get; private set; }

    public string InputSql { get; set; } = SampleSql;

    public string StatusMessage { get; private set; } = string.Empty;

    public bool HasParsedShape => ParseResult?.IsSuccess == true;

    public IReadOnlySet<string> SelectedFieldKeys => selectedFieldKeys;

    public IReadOnlySet<string> ActiveAliases { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void LoadInput()
    {
        ParseResult = parser.Parse(InputSql);
        selectedFieldKeys.Clear();
        generatedSqlDirty = true;

        if (ParseResult.IsSuccess)
        {
            foreach (var field in ParseResult.Select.Fields)
            {
                selectedFieldKeys.Add(field.Key);
            }

            RefreshDependencies();
            StatusMessage = $"Loaded {ParseResult.Select.Fields.Count} projected field(s) and {ParseResult.Select.Joins.Count} join(s).";
            return;
        }

        ActiveAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StatusMessage = ParseResult.ErrorMessage;
    }

    public void ToggleField(string key, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (isSelected)
        {
            selectedFieldKeys.Add(key);
        }
        else
        {
            selectedFieldKeys.Remove(key);
        }

        RefreshDependencies();
    }

    public void ToggleJoin(ObjectShapeJoinRow row, bool isSelected)
    {
        if (ParseResult is null)
        {
            return;
        }

        var joinFields = ParseResult.Select.Fields
            .Where(field => FieldUsesAlias(field, row.RightAlias))
            .Select(field => field.Key)
            .ToList();

        foreach (var key in joinFields)
        {
            if (isSelected)
            {
                selectedFieldKeys.Add(key);
            }
            else
            {
                selectedFieldKeys.Remove(key);
            }
        }

        RefreshDependencies();
    }

    public void SelectAllFields()
    {
        if (ParseResult is null)
        {
            return;
        }

        selectedFieldKeys.Clear();
        foreach (var field in ParseResult.Select.Fields)
        {
            selectedFieldKeys.Add(field.Key);
        }

        RefreshDependencies();
    }

    public void ClearLookupFields()
    {
        if (ParseResult is null)
        {
            return;
        }

        foreach (var field in ParseResult.Select.Fields.Where(field => field.Role == "Lookup value"))
        {
            selectedFieldKeys.Remove(field.Key);
        }

        RefreshDependencies();
    }

    public IReadOnlyList<ObjectShapeFieldRow> GetFieldRows(string filter)
    {
        if (ParseResult is null || !ParseResult.IsSuccess)
        {
            return Array.Empty<ObjectShapeFieldRow>();
        }

        return ParseResult.Select.Fields
            .Select(field => new ObjectShapeFieldRow(
                field,
                selectedFieldKeys.Contains(field.Key),
                ResolveSourceText(field),
                IsFilterMatch(field, filter)))
            .Where(row => row.IsFilterMatch)
            .ToList();
    }

    public IReadOnlyList<ObjectShapeJoinRow> GetJoinRows(string filter)
    {
        if (ParseResult is null || !ParseResult.IsSuccess)
        {
            return Array.Empty<ObjectShapeJoinRow>();
        }

        return ParseResult.Select.Joins
            .Select(join =>
            {
                var projected = ParseResult.Select.Fields
                    .Where(field => FieldUsesAlias(field, join.RightAlias))
                    .ToList();
                var selected = projected
                    .Where(field => selectedFieldKeys.Contains(field.Key))
                    .Select(field => field.OutputName)
                    .ToList();
                var isRequiredByWhere = ParseResult.Select.WhereColumnRefs
                    .Any(reference => string.Equals(reference.Alias, join.RightAlias, StringComparison.OrdinalIgnoreCase));
                var isActive = ActiveAliases.Contains(join.RightAlias);
                return new ObjectShapeJoinRow(
                    join,
                    isActive,
                    isRequiredByWhere,
                    selected,
                    projected.Select(field => field.OutputName).ToList(),
                    IsFilterMatch(join, selected, filter));
            })
            .Where(row => row.IsFilterMatch)
            .ToList();
    }

    public string EnsureGeneratedSql()
    {
        if (ParseResult is null)
        {
            return string.Empty;
        }

        if (generatedSqlDirty)
        {
            generatedSql = parser.GenerateSql(ParseResult, selectedFieldKeys);
            generatedSqlDirty = false;
        }

        return generatedSql;
    }

    private void RefreshDependencies()
    {
        if (ParseResult is null || !ParseResult.IsSuccess)
        {
            ActiveAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            generatedSqlDirty = true;
            return;
        }

        ActiveAliases = parser.ResolveRequiredAliases(ParseResult.Select, selectedFieldKeys);
        generatedSqlDirty = true;

        var activeJoins = ParseResult.Select.Joins.Count(join => ActiveAliases.Contains(join.RightAlias));
        StatusMessage = $"{selectedFieldKeys.Count} field(s) selected. Generated SQL will include {activeJoins} join(s).";
    }

    private string ResolveSourceText(ObjectShapeField field)
    {
        if (ParseResult is null || string.IsNullOrWhiteSpace(field.SourceAlias))
        {
            return "(expression)";
        }

        var source = ParseResult.Select.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, field.SourceAlias, StringComparison.OrdinalIgnoreCase));
        return source is null
            ? field.SourceAlias
            : $"{source.ObjectName} AS {source.Alias}";
    }

    private static bool FieldUsesAlias(ObjectShapeField field, string alias)
    {
        return field.ColumnRefs.Any(reference =>
            string.Equals(reference.Alias, alias, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFilterMatch(ObjectShapeField field, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(field.OutputName, filter)
            || Contains(field.ExpressionText, filter)
            || Contains(field.Role, filter)
            || Contains(field.SourceAlias, filter)
            || Contains(field.SourceColumn, filter);
    }

    private static bool IsFilterMatch(ObjectShapeJoin join, IReadOnlyList<string> selectedProjectionNames, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(join.DisplayText, filter)
            || Contains(join.RightAlias, filter)
            || selectedProjectionNames.Any(name => Contains(name, filter));
    }

    private static bool Contains(string? text, string filter)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public const string SampleSql = """
CREATE OR ALTER VIEW [dbo].[SS_ExcedeSchema_ACMEM]
AS
SELECT
      [acmem].[MemId]
    , [acmem].[AcId]
    , [acmem].[AcName]
    , [acmem].[Des]
    , [acmem].[Amt]
    , [acmem].[DateCreate]
    , [acmem].[JeId]
    , [acmem].[Posted]
    , [acmem].[EmpId]
    , [COEMP_EmpId].[Name] AS [EmpDescription]
    , [acmem].[TrmId]
    , [COTRM_TrmId].[Des] AS [TrmDescription]
    , [acmem].[BrnId]
    , [COBRN_BrnId].[Name] AS [BrnDescription]
    , [acmem].[ACTYP]
    , [COLOOKUP_ACTYP].[Des1] AS [ACTYPDescription]
FROM [ExcedeSchema].[dbo].[acmem] AS [acmem]
INNER JOIN [ExcedeSchema].[dbo].[COEMP] AS [COEMP_EmpId] ON [COEMP_EmpId].[EmpId] = [acmem].[EmpId]
INNER JOIN [ExcedeSchema].[dbo].[COTRM] AS [COTRM_TrmId] ON [COTRM_TrmId].[TrmId] = [acmem].[TrmId]
INNER JOIN [ExcedeSchema].[dbo].[COBRN] AS [COBRN_BrnId] ON [COBRN_BrnId].[BrnId] = [acmem].[BrnId]
INNER JOIN [ExcedeSchema].[dbo].[COLOOKUP] AS [COLOOKUP_ACTYP] ON [COLOOKUP_ACTYP].[Id] = [acmem].[ACTYP] AND [COLOOKUP_ACTYP].[Name] = 'ACMEM_ACTYP'
""";
}

public sealed record ObjectShapeFieldRow(
    ObjectShapeField Field,
    bool IsSelected,
    string SourceText,
    bool IsFilterMatch);

public sealed record ObjectShapeJoinRow(
    ObjectShapeJoin Join,
    bool IsActive,
    bool IsRequiredByWhere,
    IReadOnlyList<string> SelectedProjectionNames,
    IReadOnlyList<string> AvailableProjectionNames,
    bool IsFilterMatch)
{
    public string RightAlias => Join.RightAlias;

    public string Status => IsActive
        ? IsRequiredByWhere ? "Required by WHERE" : "Active"
        : "Not used";

    public string ProjectionSummary => SelectedProjectionNames.Count > 0
        ? $"Projects {string.Join(", ", SelectedProjectionNames)}"
        : AvailableProjectionNames.Count > 0
            ? $"Available {string.Join(", ", AvailableProjectionNames)}"
            : "No direct projection";
}
