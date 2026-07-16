using SchemaStudioWebViewer.Data;

[module: SchemaStudio.AIHelpers.FileVersion("1.2")]
[module: SchemaStudio.AIHelpers.AIFileContext(
    "Components/Pages/BaseViewCreator/BaseViewCreator.Sql.cs",
    "Partial class slice for Base View Creator SQL and projection generation.",
    Responsibilities = "Owns generated SQL assembly, projection-spec construction, output column enumeration, CREATE TABLE emission, and join dependency emission for the Base View Creator fork.",
    Nuances = "This file deliberately depends on state and UI helpers still housed in BaseViewCreator.razor; it is the first mechanical split toward a smaller Razor surface.",
    RelatedFiles = "Components/Pages/BaseViewCreator/BaseViewCreator.razor; Components/Pages/BaseViewCreator/BaseViewCreatorSelectionEngine.cs; Components/Pages/BaseViewCreator/UdtTypeResolver.cs",
    LastReviewed = "2026-07-16")]

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

public partial class BaseViewCreator
{
    private void RegenerateSql()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabaseName) ||
            string.IsNullOrWhiteSpace(SelectedSchemaName) ||
            string.IsNullOrWhiteSpace(SelectedTableName) ||
            string.IsNullOrWhiteSpace(BaseAlias) ||
            string.IsNullOrWhiteSpace(TargetDatabaseName) ||
            string.IsNullOrWhiteSpace(TargetSchemaName) ||
            string.IsNullOrWhiteSpace(TargetViewName))
        {
            GeneratedSql = "";
            GeneratedCreateTableSql = "";
            SqlRenderVersion++;
            SqlDirty = false;
            return;
        }

        var projectionSpecs = BuildProjectionSpecs().ToList();
        var lines = OutputShape == OutputShapeStandard
            ? BuildStandardSql(projectionSpecs)
            : BuildCteSql(projectionSpecs);

        GeneratedSql = string.Join(Environment.NewLine, lines);
        GeneratedCreateTableSql = string.Join(Environment.NewLine, BuildCreateTableSql(projectionSpecs));
        SqlRenderVersion++;
        SqlDirty = false;
        QueueSqlHighlight();
    }

    private List<string> BuildStandardSql(IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        var lines = new List<string>
        {
            $"CREATE OR ALTER VIEW {TargetViewNameSql}",
            "AS",
            "SELECT"
        };

        var projectionLines = BuildProjectionLines(projectionSpecs).ToList();
        AddProjectionLines(lines, projectionLines, "      -- Select at least one column.");
        AddFromAndJoinLines(lines, "", projectionSpecs);
        return lines;
    }

    private List<string> BuildCteSql(IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        var sourceCteName = BuildSourceCteName();
        var lines = new List<string>
        {
            $"CREATE OR ALTER VIEW {TargetViewNameSql}",
            "AS",
            "",
            $"WITH {QuoteIdentifier(sourceCteName)} AS",
            "(",
            "    SELECT"
        };

        AddProjectionLines(lines, BuildCteSourceProjectionLines(projectionSpecs).ToList(), "          -- Select at least one column.", "    ");
        AddFromAndJoinLines(lines, "    ", projectionSpecs);
        lines.Add(")");
        lines.Add("");
        lines.Add("SELECT");

        if (projectionSpecs.Count == 0)
        {
            lines.Add("      -- Select at least one column.");
        }
        else
        {
            foreach (var line in BuildCteSurfaceProjectionLines(sourceCteName, projectionSpecs))
            {
                lines.Add(line);
            }
        }

        lines.Add($"FROM {QuoteIdentifier(sourceCteName)};");
        return lines;
    }

    private List<string> BuildCreateTableSql(IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        var lines = new List<string>
        {
            $"CREATE TABLE {TargetTableNameSql}",
            "("
        };

        if (projectionSpecs.Count == 0)
        {
            lines.Add("      -- Select at least one column.");
        }
        else
        {
            var first = true;
            foreach (var projection in projectionSpecs)
            {
                var dataType = string.IsNullOrWhiteSpace(projection.SqlDataType)
                    ? "nvarchar(255)"
                    : projection.SqlDataType;
                var nullability = projection.IsNullable ? "NULL" : "NOT NULL";
                lines.Add($"{ProjectionPrefix(first)}{QuoteIdentifier(projection.OutputColumnName)} {dataType} {nullability}");
                first = false;
            }
        }

        lines.Add(");");
        return lines;
    }

    private void AddProjectionLines(ICollection<string> lines, IReadOnlyList<string> projectionLines, string emptyProjectionLine, string linePrefix = "")
    {
        if (projectionLines.Count == 0)
        {
            lines.Add($"{linePrefix}{emptyProjectionLine}");
            return;
        }

        foreach (var projectionLine in projectionLines)
        {
            lines.Add(string.IsNullOrWhiteSpace(projectionLine)
                ? ""
                : $"{linePrefix}{projectionLine}");
        }
    }

    private void AddFromAndJoinLines(ICollection<string> lines, string linePrefix, IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        lines.Add("");
        lines.Add($"{linePrefix}FROM {QualifiedName(SelectedDatabaseName, SelectedSchemaName, SelectedTableName)} AS {QuoteIdentifier(BaseAlias)}");

        foreach (var relationship in GetProjectionJoinDependencies(projectionSpecs))
        {
            foreach (var joinLine in BuildJoinLines(relationship))
            {
                lines.Add($"{linePrefix}{joinLine}");
            }
        }
    }

    private IEnumerable<string> BuildProjectionLines(IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        var first = true;

        foreach (var projection in projectionSpecs)
        {
            if (!first && projection.StartsRelationshipGroup)
            {
                yield return "";
            }

            yield return BuildProjectionLine(
                ProjectionPrefix(first),
                projection.SourceProjection,
                projection.OutputColumnName,
                projection.BusinessName,
                projection.BusinessDescription,
                projection.DisableInheritance);
            first = false;
        }
    }

    private IEnumerable<string> BuildCteSourceProjectionLines(IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        var first = true;

        foreach (var projection in projectionSpecs)
        {
            if (!first && projection.StartsRelationshipGroup)
            {
                yield return "";
            }

            yield return $"{ProjectionPrefix(first)}{projection.SourceProjection}";
            first = false;
        }
    }

    private IEnumerable<string> BuildCteSurfaceProjectionLines(string sourceCteName, IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        var first = true;

        foreach (var projection in projectionSpecs)
        {
            if (!first && projection.StartsRelationshipGroup)
            {
                yield return "";
            }

            var surfaceProjection = $"{QuoteIdentifier(sourceCteName)}.{QuoteIdentifier(projection.OutputColumnName)}";
            yield return BuildProjectionLine(
                ProjectionPrefix(first),
                surfaceProjection,
                projection.OutputColumnName,
                projection.BusinessName,
                projection.BusinessDescription,
                projection.DisableInheritance);
            first = false;
        }
    }

    private IEnumerable<ProjectionSpec> BuildProjectionSpecs()
    {
        var plan = CurrentSelectionPlan;
        var emittedRelationshipColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in plan.BaseColumns)
        {
            var projection = $"{QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(column.ColumnName)}";
            yield return new ProjectionSpec(projection, column.ColumnName, column.BusinessName, column.BusinessDescription, false, false, null, UdtTypeResolver.ResolveByName(column.DataType), column.IsNullable);
        }

        foreach (var state in plan.JoinDependencies)
        {
            var relationship = state.Relationship;
            var startsRelationshipGroup = true;
            foreach (var pair in relationship.Columns)
            {
                if (!plan.IsColumnSelected(pair.LocalColumnName) || !emittedRelationshipColumns.Add(pair.LocalColumnName))
                {
                    continue;
                }

                var fkOutputName = $"{pair.LocalColumnName}_FK";
                var projection = $"{QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(pair.LocalColumnName)} AS {QuoteIdentifier(fkOutputName)}";
                var column = Columns.FirstOrDefault(column => string.Equals(column.ColumnName, pair.LocalColumnName, StringComparison.OrdinalIgnoreCase));
                yield return new ProjectionSpec(projection, fkOutputName, column?.BusinessName ?? "", column?.BusinessDescription ?? "", true, startsRelationshipGroup, null, UdtTypeResolver.ResolveByName(column?.DataType ?? ""), column?.IsNullable ?? true);
                startsRelationshipGroup = false;
            }

            if (!relationship.IncludeDisplayColumn ||
                string.IsNullOrWhiteSpace(relationship.DisplayColumnName))
            {
                continue;
            }

            var alias = BuildLookupAlias(relationship);
            var columnAlias = BuildLookupProjectionAlias(relationship);
            var lookupProjection = $"{QuoteIdentifier(alias)}.{QuoteIdentifier(relationship.DisplayColumnName!)} AS {QuoteIdentifier(columnAlias)}";
            var displayNullable = string.Equals(relationship.SelectedJoinType, "LEFT JOIN", StringComparison.OrdinalIgnoreCase) || GetDisplayColumnIsNullable(relationship);
            yield return new ProjectionSpec(lookupProjection, columnAlias, relationship.DisplayBusinessName, relationship.DisplayBusinessDescription, true, false, relationship, GetDisplayColumnDataType(relationship), displayNullable);
        }
    }

    private IEnumerable<TableSchemaRelationshipInfo> GetProjectionJoinDependencies(IReadOnlyList<ProjectionSpec> projectionSpecs)
    {
        foreach (var relationship in CurrentSelectionPlan.JoinDependencies.Select(state => state.Relationship))
        {
            if (projectionSpecs.Any(projection => ReferenceEquals(projection.LookupJoinRelationship, relationship)))
            {
                yield return relationship;
            }
        }
    }

    private IEnumerable<string> BuildOutputColumnNames()
    {
        var plan = CurrentSelectionPlan;
        var emittedRelationshipColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in plan.BaseColumns)
        {
            yield return column.ColumnName;
        }

        foreach (var state in plan.JoinDependencies)
        {
            var relationship = state.Relationship;
            foreach (var pair in relationship.Columns)
            {
                if (!plan.IsColumnSelected(pair.LocalColumnName) || !emittedRelationshipColumns.Add(pair.LocalColumnName))
                {
                    continue;
                }

                yield return $"{pair.LocalColumnName}_FK";
            }

            if (relationship.IncludeDisplayColumn &&
                !string.IsNullOrWhiteSpace(relationship.DisplayColumnName))
            {
                yield return BuildLookupProjectionAlias(relationship);
            }
        }
    }

    private sealed record ProjectionSpec(
        string SourceProjection,
        string OutputColumnName,
        string BusinessName,
        string BusinessDescription,
        bool DisableInheritance,
        bool StartsRelationshipGroup,
        TableSchemaRelationshipInfo? LookupJoinRelationship,
        string SqlDataType,
        bool IsNullable);
}
