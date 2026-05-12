using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Data;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewGenerator;

[FileVersion("1.0")]
[AIFileContext(
    "Components/Pages/BaseViewGenerator/BaseViewProjectionPlanner.cs",
    "Projection planning engine for Base View Generator.",
    Responsibilities = "Build a single dependency-aware projection plan from source columns and lookup relationships so Razor markup and SQL generation do not independently rediscover row ownership, metadata defaults, and join dependencies.",
    Nuances = "Lookup discovery remains upstream in TableSchemaSmoRepository and the database lookup repository. This planner only reasons over already-loaded column and relationship state.")]
public sealed class BaseViewProjectionPlanner
{
    public BaseViewProjectionPlan Build(
        IReadOnlyList<TableSchemaColumnInfo> columns,
        IReadOnlyList<TableSchemaRelationshipInfo> relationships,
        string baseAlias,
        Func<string, string> quoteIdentifier,
        Func<TableSchemaRelationshipInfo, string> buildLookupAlias,
        Func<TableSchemaRelationshipInfo, string> buildLookupProjectionAlias,
        Func<TableSchemaRelationshipInfo, bool> shouldGenerateJoin)
    {
        var includedColumns = columns
            .Where(column => column.Include)
            .ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        var relationshipDisplayState = relationships
            .ToDictionary(
                relationship => relationship,
                relationship => RelationshipHasLookupDisplayProjection(relationship, includedColumns, shouldGenerateJoin));
        var emittedRelationshipColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<BaseViewProjectionRow>();
        var sourceRows = new List<BaseViewProjectionRow>();
        var lookupRows = new List<BaseViewProjectionRow>();

        foreach (var column in columns.Where(column => column.Include && !ColumnHasLookupDisplayProjection(column.ColumnName, relationships, relationshipDisplayState)))
        {
            var row = BuildSourceColumnRow(column, baseAlias, quoteIdentifier);
            rows.Add(row);
            sourceRows.Add(row);
        }

        foreach (var relationship in relationships.Where(relationshipDisplayState.GetValueOrDefault))
        {
            var startsRelationshipGroup = true;
            foreach (var pair in relationship.Columns)
            {
                if (!includedColumns.TryGetValue(pair.LocalColumnName, out var column) ||
                    !emittedRelationshipColumns.Add(pair.LocalColumnName))
                {
                    continue;
                }

                var projection = $"{quoteIdentifier(baseAlias)}.{quoteIdentifier(pair.LocalColumnName)}";
                var row = new BaseViewProjectionRow(
                    projection,
                    pair.LocalColumnName,
                    column.BusinessName,
                    column.BusinessDescription,
                    true,
                    startsRelationshipGroup,
                    relationship,
                    column);
                rows.Add(row);
                sourceRows.Add(row);
                startsRelationshipGroup = false;
            }

            if (!shouldGenerateJoin(relationship) ||
                !relationship.IncludeDisplayColumn ||
                string.IsNullOrWhiteSpace(relationship.DisplayColumnName))
            {
                continue;
            }

            var alias = buildLookupAlias(relationship);
            var columnAlias = buildLookupProjectionAlias(relationship);
            var lookupProjection = $"{quoteIdentifier(alias)}.{quoteIdentifier(relationship.DisplayColumnName!)} AS {quoteIdentifier(columnAlias)}";
            var lookupRow = new BaseViewProjectionRow(
                lookupProjection,
                columnAlias,
                relationship.DisplayBusinessName,
                relationship.DisplayBusinessDescription,
                true,
                false,
                relationship,
                null);
            rows.Add(lookupRow);
            lookupRows.Add(lookupRow);
        }

        var joinDependencies = rows
            .Where(row => row.LookupJoinRelationship is not null)
            .Select(row => row.LookupJoinRelationship!)
            .Distinct(ReferenceEqualityComparer<TableSchemaRelationshipInfo>.Instance)
            .ToList();
        var outputColumnNames = rows
            .Select(row => row.OutputColumnName)
            .ToList();

        return new BaseViewProjectionPlan(rows, sourceRows, lookupRows, joinDependencies, outputColumnNames, relationshipDisplayState);
    }

    private static BaseViewProjectionRow BuildSourceColumnRow(
        TableSchemaColumnInfo column,
        string baseAlias,
        Func<string, string> quoteIdentifier)
    {
        var projection = $"{quoteIdentifier(baseAlias)}.{quoteIdentifier(column.ColumnName)}";
        return new BaseViewProjectionRow(projection, column.ColumnName, column.BusinessName, column.BusinessDescription, false, false, null, column);
    }

    private static bool RelationshipHasLookupDisplayProjection(
        TableSchemaRelationshipInfo relationship,
        IReadOnlyDictionary<string, TableSchemaColumnInfo> includedColumns,
        Func<TableSchemaRelationshipInfo, bool> shouldGenerateJoin)
    {
        return shouldGenerateJoin(relationship) &&
            relationship.IncludeDisplayColumn &&
            !string.IsNullOrWhiteSpace(relationship.DisplayColumnName) &&
            relationship.Columns.Any(pair => includedColumns.ContainsKey(pair.LocalColumnName));
    }

    private static bool ColumnHasLookupDisplayProjection(
        string columnName,
        IReadOnlyList<TableSchemaRelationshipInfo> relationships,
        IReadOnlyDictionary<TableSchemaRelationshipInfo, bool> relationshipDisplayState)
    {
        return relationships.Any(relationship =>
            relationshipDisplayState.GetValueOrDefault(relationship) &&
            relationship.Columns.Any(pair => string.Equals(pair.LocalColumnName, columnName, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed record BaseViewProjectionPlan(
    IReadOnlyList<BaseViewProjectionRow> Rows,
    IReadOnlyList<BaseViewProjectionRow> SourceRows,
    IReadOnlyList<BaseViewProjectionRow> LookupRows,
    IReadOnlyList<TableSchemaRelationshipInfo> JoinDependencies,
    IReadOnlyList<string> OutputColumnNames,
    IReadOnlyDictionary<TableSchemaRelationshipInfo, bool> RelationshipDisplayState)
{
    public static BaseViewProjectionPlan Empty { get; } = new(
        Array.Empty<BaseViewProjectionRow>(),
        Array.Empty<BaseViewProjectionRow>(),
        Array.Empty<BaseViewProjectionRow>(),
        Array.Empty<TableSchemaRelationshipInfo>(),
        Array.Empty<string>(),
        new Dictionary<TableSchemaRelationshipInfo, bool>());
}

public sealed record BaseViewProjectionRow(
    string SourceProjection,
    string OutputColumnName,
    string BusinessName,
    string BusinessDescription,
    bool DisableInheritance,
    bool StartsRelationshipGroup,
    TableSchemaRelationshipInfo? LookupJoinRelationship,
    TableSchemaColumnInfo? SourceColumn);

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    public bool Equals(T? x, T? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
