using SchemaStudioWebViewer.Data;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

public partial class BaseViewCreator
{
    private const string CountryDbColumnName = "CountryDB";
    private const string CountryDbColumnType = "varchar(128)";
    private const string RowVersionColumnName = "TS";
    private const string SourceTsColumnName = "SourceTS";

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
            GeneratedMergeSql = "";
            MergeUnavailableReason = "";
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

    private void RegenerateMerge()
    {
        var projectionSpecs = BuildProjectionSpecs().ToList();
        GeneratedMergeSql = string.Join(Environment.NewLine, BuildMergeSql(projectionSpecs, out var reason));
        MergeUnavailableReason = reason;
        SqlRenderVersion++;
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
            lines.Add(");");
            return lines;
        }

        lines.Add($"{ProjectionPrefix(true)}{QuoteIdentifier(CountryDbColumnName)} {CountryDbColumnType} NOT NULL");
        foreach (var projection in projectionSpecs)
        {
            var dataType = ResolveCreateTableType(projection);
            var nullability = projection.IsNullable ? "NULL" : "NOT NULL";
            lines.Add($"{ProjectionPrefix(false)}{QuoteIdentifier(DestColumnName(projection))} {dataType} {nullability}");
        }

        var keySpecs = projectionSpecs.Where(spec => spec.IsPrimaryKey).ToList();
        if (keySpecs.Count > 0)
        {
            var keyColumns = new List<string> { QuoteIdentifier(CountryDbColumnName) };
            keyColumns.AddRange(keySpecs.Select(spec => QuoteIdentifier(DestColumnName(spec))));
            var tableToken = SanitizeAliasToken(string.IsNullOrWhiteSpace(TargetTableName) ? TargetViewName : TargetTableName);
            lines.Add($"{ProjectionPrefix(false)}CONSTRAINT {QuoteIdentifier($"PK_{tableToken}")} PRIMARY KEY CLUSTERED ({string.Join(", ", keyColumns)})");
        }

        lines.Add(");");
        return lines;
    }

    private static string ResolveCreateTableType(ProjectionSpec projection)
    {
        if (string.Equals(projection.OutputColumnName, RowVersionColumnName, StringComparison.OrdinalIgnoreCase))
        {
            return "bigint";
        }

        return string.IsNullOrWhiteSpace(projection.SqlDataType)
            ? "nvarchar(255)"
            : projection.SqlDataType;
    }

    private static string DestColumnName(ProjectionSpec spec) =>
        string.Equals(spec.OutputColumnName, RowVersionColumnName, StringComparison.OrdinalIgnoreCase)
            ? SourceTsColumnName
            : spec.OutputColumnName;

    private List<string> BuildMergeSql(IReadOnlyList<ProjectionSpec> projectionSpecs, out string unavailableReason)
    {
        unavailableReason = "";

        if (projectionSpecs.Count == 0)
        {
            unavailableReason = "Select at least one column to build the merge.";
            return new List<string>();
        }

        var tsSpec = projectionSpecs.FirstOrDefault(spec => string.Equals(spec.OutputColumnName, RowVersionColumnName, StringComparison.OrdinalIgnoreCase));
        if (tsSpec is null)
        {
            unavailableReason = $"The projection does not include a {RowVersionColumnName} (rowversion) column. Add the base table's {RowVersionColumnName} column to the Select Projection to enable the incremental merge.";
            return new List<string>();
        }

        var keySpecs = projectionSpecs.Where(spec => spec.IsPrimaryKey).ToList();
        if (keySpecs.Count == 0)
        {
            unavailableReason = "No primary-key column is included in the projection. A key column is required to match target rows.";
            return new List<string>();
        }

        var countryDatabase = string.IsNullOrWhiteSpace(MergeCountryDb) ? SelectedDatabaseName : MergeCountryDb.Trim();
        var sourceDatabase = string.IsNullOrWhiteSpace(MergeSourceDb) ? SelectedDatabaseName : MergeSourceDb.Trim();
        var sourceSchema = string.IsNullOrWhiteSpace(MergeSourceSchema) ? SelectedSchemaName : MergeSourceSchema.Trim();
        var destinationTable = QualifiedName(MergeDestinationDb, MergeDestinationSchema, string.IsNullOrWhiteSpace(MergeDestinationTable) ? TargetViewName : MergeDestinationTable);

        var lines = new List<string>
        {
            $"MERGE INTO {destinationTable} AS tgt",
            "USING",
            "(",
            "    SELECT"
        };

        var firstSourceColumn = true;
        foreach (var projection in projectionSpecs)
        {
            lines.Add($"    {ProjectionPrefix(firstSourceColumn)}{projection.SourceProjection}");
            firstSourceColumn = false;
        }

        lines.Add($"    {ProjectionPrefix(false)}{QuoteSqlLiteral(countryDatabase)} AS {QuoteIdentifier(CountryDbColumnName)}");
        lines.Add($"    FROM {QualifiedName(sourceDatabase, sourceSchema, SelectedTableName)} AS {QuoteIdentifier(BaseAlias)}");
        foreach (var relationship in GetProjectionJoinDependencies(projectionSpecs))
        {
            foreach (var joinLine in BuildMergeJoinLines(relationship, sourceDatabase))
            {
                lines.Add($"    {joinLine}");
            }
        }

        lines.Add(") AS src");

        var keyConditions = new List<string>
        {
            $"tgt.{QuoteIdentifier(CountryDbColumnName)} = src.{QuoteIdentifier(CountryDbColumnName)}"
        };
        keyConditions.AddRange(keySpecs.Select(spec => $"tgt.{QuoteIdentifier(spec.OutputColumnName)} = src.{QuoteIdentifier(spec.OutputColumnName)}"));

        var firstCondition = true;
        foreach (var condition in keyConditions)
        {
            lines.Add(firstCondition ? $"    ON {condition}" : $"        AND {condition}");
            firstCondition = false;
        }

        var updateSpecs = projectionSpecs.Where(spec => !spec.IsPrimaryKey).ToList();
        if (updateSpecs.Count > 0)
        {
            lines.Add($"WHEN MATCHED AND {MergeSourceValue(tsSpec)} > tgt.{QuoteIdentifier(DestColumnName(tsSpec))} THEN");
            lines.Add("    UPDATE SET");
            var firstUpdate = true;
            foreach (var spec in updateSpecs)
            {
                lines.Add($"{ProjectionPrefix(firstUpdate)}tgt.{QuoteIdentifier(DestColumnName(spec))} = {MergeSourceValue(spec)}");
                firstUpdate = false;
            }
        }

        lines.Add("WHEN NOT MATCHED BY TARGET THEN");
        lines.Add("    INSERT");
        lines.Add("    (");
        lines.Add($"{ProjectionPrefix(true)}{QuoteIdentifier(CountryDbColumnName)}");
        foreach (var spec in projectionSpecs)
        {
            lines.Add($"{ProjectionPrefix(false)}{QuoteIdentifier(DestColumnName(spec))}");
        }

        lines.Add("    )");
        lines.Add("    VALUES");
        lines.Add("    (");
        lines.Add($"{ProjectionPrefix(true)}src.{QuoteIdentifier(CountryDbColumnName)}");
        foreach (var spec in projectionSpecs)
        {
            lines.Add($"{ProjectionPrefix(false)}{MergeSourceValue(spec)}");
        }

        lines.Add("    );");

        return lines;
    }

    private static string MergeSourceValue(ProjectionSpec spec)
    {
        if (string.Equals(spec.OutputColumnName, RowVersionColumnName, StringComparison.OrdinalIgnoreCase))
        {
            return $"CONVERT(bigint, src.{QuoteIdentifier(spec.OutputColumnName)})";
        }

        return $"src.{QuoteIdentifier(spec.OutputColumnName)}";
    }

    private IEnumerable<string> BuildMergeJoinLines(TableSchemaRelationshipInfo relationship, string sourceDatabase)
    {
        var alias = BuildLookupAlias(relationship);
        var predicates = relationship.Columns
            .Select(pair => $"{QuoteIdentifier(alias)}.{QuoteIdentifier(pair.ReferencedColumnName)} = {QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(pair.LocalColumnName)}");
        if (!string.IsNullOrWhiteSpace(relationship.LookupFilterColumnName) &&
            !string.IsNullOrWhiteSpace(relationship.LookupFilterValue))
        {
            predicates = predicates.Append($"{QuoteIdentifier(alias)}.{QuoteIdentifier(relationship.LookupFilterColumnName)} = {QuoteSqlLiteral(relationship.LookupFilterValue)}");
        }

        yield return $"{relationship.SelectedJoinType} {QualifiedName(sourceDatabase, relationship.ReferencedSchemaName, relationship.ReferencedTableName)} AS {QuoteIdentifier(alias)} ON {string.Join(" AND ", predicates)}";
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
            yield return new ProjectionSpec(projection, column.ColumnName, column.BusinessName, column.BusinessDescription, false, false, null, UdtTypeResolver.ResolveByName(column.DataType), column.IsNullable, column.IsPrimaryKey);
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
                yield return new ProjectionSpec(projection, fkOutputName, column?.BusinessName ?? "", column?.BusinessDescription ?? "", true, startsRelationshipGroup, null, UdtTypeResolver.ResolveByName(column?.DataType ?? ""), column?.IsNullable ?? true, false);
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
            yield return new ProjectionSpec(lookupProjection, columnAlias, relationship.DisplayBusinessName, relationship.DisplayBusinessDescription, true, false, relationship, GetDisplayColumnDataType(relationship), displayNullable, false);
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
        bool IsNullable,
        bool IsPrimaryKey);
}
