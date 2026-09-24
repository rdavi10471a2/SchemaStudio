using SchemaStudioWebViewer.Data;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

public partial class BaseViewCreator
{
    private const string SourceDbColumnName = "SourceDB";
    private const string SourceDbColumnType = "nvarchar(128)";
    private const string TableNameColumnName = "TableName";
    private const string TableNameColumnType = "nvarchar(100)";
    private const string RowVersionColumnName = "TS";
    private const string SourceTsColumnName = "SourceTS";

    // Date-filter range tokens (stored in MergeDateFilterRange). Keep in sync with the Merge UI dropdown.
    private const string DateRange7 = "7d";
    private const string DateRange30 = "30d";
    private const string DateRange60 = "60d";
    private const string DateRangePriorMonth = "priorMonthStart";
    private const string DateRangeCurrentMonth = "currentMonthStart";

    // Sentinel token the batch (looped) merge template carries in place of a concrete source database.
    // The generated cursor REPLACEs it per control-table row in two forms: bracketed [__SOURCE_DB__] in
    // the FROM/JOIN identifiers, and quoted '__SOURCE_DB__' as the [SourceDB] column value.
    private const string BatchSourceDbToken = "__SOURCE_DB__";

    // Surrogate key (Power BI single-column join) tokens. A surrogate is CONCAT([SourceDB], '|',
    // CAST(key1 AS nvarchar), '|', CAST(key2 ...)) so a composite/multi-source key collapses to one
    // deterministic column. Emitted identically on both sides of a join so Power BI can match them.
    private const string SurrogateKeySuffix = "_SK_PBI";
    private const string SurrogateKeyDelimiter = "|";

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

    private void RegenerateBatchMerge()
    {
        var projectionSpecs = BuildProjectionSpecs().ToList();
        GeneratedBatchMergeSql = string.Join(Environment.NewLine, BuildBatchMergeSql(projectionSpecs, out var reason));
        BatchMergeUnavailableReason = reason;
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

        // Schema Studio control columns always lead the projection, in the SAME order the
        // CREATE TABLE and MERGE selects emit them: [SourceDB], [TableName], then the columns.
        lines.AddRange(BuildControlProjectionLines());

        var projectionLines = BuildProjectionLines(projectionSpecs, leadingColumnsEmitted: true).ToList();
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

        // Schema Studio control columns always lead the surface projection.
        lines.AddRange(BuildControlProjectionLines());

        if (projectionSpecs.Count == 0)
        {
            lines.Add("      -- Select at least one column.");
        }
        else
        {
            foreach (var line in BuildCteSurfaceProjectionLines(sourceCteName, projectionSpecs, leadingColumnsEmitted: true))
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
            $"DROP TABLE IF EXISTS {TargetTableNameSql};",
            "",
            $"CREATE TABLE {TargetTableNameSql}",
            "("
        };

        if (projectionSpecs.Count == 0)
        {
            lines.Add("      -- Select at least one column.");
            lines.Add(");");
            return lines;
        }

        lines.Add($"{ProjectionPrefix(true)}{QuoteIdentifier(SourceDbColumnName)} {SourceDbColumnType} NOT NULL");
        lines.Add($"{ProjectionPrefix(false)}{QuoteIdentifier(TableNameColumnName)} {TableNameColumnType} NOT NULL");
        foreach (var projection in projectionSpecs)
        {
            var dataType = ResolveCreateTableType(projection);
            var nullability = projection.IsNullable ? "NULL" : "NOT NULL";
            lines.Add($"{ProjectionPrefix(false)}{QuoteIdentifier(DestColumnName(projection))} {dataType} {nullability}");
        }

        var keySpecs = projectionSpecs.Where(spec => spec.IsPrimaryKey).ToList();
        if (keySpecs.Count > 0)
        {
            var keyColumns = new List<string> { QuoteIdentifier(SourceDbColumnName) };
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
        if (!TryPrepareMergeSpecs(projectionSpecs, out var tsSpec, out var keySpecs, out unavailableReason))
        {
            return new List<string>();
        }

        var countryDatabase = string.IsNullOrWhiteSpace(MergeCountryDb) ? SelectedDatabaseName : MergeCountryDb.Trim();
        var sourceDatabase = string.IsNullOrWhiteSpace(MergeSourceDb) ? SelectedDatabaseName : MergeSourceDb.Trim();
        var sourceSchema = string.IsNullOrWhiteSpace(MergeSourceSchema) ? SelectedSchemaName : MergeSourceSchema.Trim();

        return BuildMergeStatementLines(projectionSpecs, keySpecs, tsSpec, QuoteSqlLiteral(countryDatabase), sourceDatabase, sourceSchema);
    }

    // Validates that the projection can drive an incremental merge and, when it can, hands back the
    // rowversion (TS) column and the primary-key columns. Shared by the single-target Merge tab and
    // the batch (looped) merge so both enforce the same preconditions.
    private bool TryPrepareMergeSpecs(
        IReadOnlyList<ProjectionSpec> projectionSpecs,
        out ProjectionSpec? tsSpec,
        out List<ProjectionSpec> keySpecs,
        out string unavailableReason)
    {
        tsSpec = null;
        keySpecs = new List<ProjectionSpec>();
        unavailableReason = "";

        if (projectionSpecs.Count == 0)
        {
            unavailableReason = "Select at least one column to build the merge.";
            return false;
        }

        // The TS rowversion column drives the incremental guard (src.TS > tgt.TS). It is only required
        // when UseRowVersionColumn is on (the default); when off, tsSpec stays null and the merge updates
        // every matched row unconditionally.
        if (UseRowVersionColumn)
        {
            var ts = projectionSpecs.FirstOrDefault(spec => string.Equals(spec.OutputColumnName, RowVersionColumnName, StringComparison.OrdinalIgnoreCase));
            if (ts is null)
            {
                unavailableReason = $"The projection does not include a {RowVersionColumnName} (rowversion) column. Add the base table's {RowVersionColumnName} column to the Select Projection to enable the incremental merge, or turn off “Use TS (rowversion) column”.";
                return false;
            }

            tsSpec = ts;
        }

        var keys = projectionSpecs.Where(spec => spec.IsPrimaryKey).ToList();
        if (keys.Count == 0)
        {
            unavailableReason = "No primary-key column is included in the projection. A key column is required to match target rows.";
            return false;
        }

        keySpecs = keys;
        return true;
    }

    // Emits one MERGE statement. countryDbSourceExpression is written verbatim into the source
    // SourceDB column (a quoted literal for a single target, or a REPLACE sentinel for the batch
    // loop). sourceDatabase qualifies the FROM/JOIN tables (a real database, or a sentinel token).
    private List<string> BuildMergeStatementLines(
        IReadOnlyList<ProjectionSpec> projectionSpecs,
        IReadOnlyList<ProjectionSpec> keySpecs,
        ProjectionSpec? tsSpec,
        string countryDbSourceExpression,
        string sourceDatabase,
        string sourceSchema)
    {
        var destinationTable = QualifiedName(MergeDestinationDb, MergeDestinationSchema, string.IsNullOrWhiteSpace(MergeDestinationTable) ? TargetViewName : MergeDestinationTable);

        var lines = new List<string>
        {
            $"MERGE INTO {destinationTable} AS tgt",
            "USING",
            "(",
            "    SELECT"
        };

        // Leading columns first, in the SAME order the CREATE TABLE emits them: SourceDB, TableName,
        // then the selected projection columns.
        lines.Add($"    {ProjectionPrefix(true)}{countryDbSourceExpression} AS {QuoteIdentifier(SourceDbColumnName)}");
        lines.Add($"    {ProjectionPrefix(false)}{QuoteSqlLiteral(SelectedTableName)} AS {QuoteIdentifier(TableNameColumnName)}");
        foreach (var projection in projectionSpecs)
        {
            lines.Add($"    {ProjectionPrefix(false)}{RetargetSurrogateSourceDb(projection.SourceProjection, countryDbSourceExpression)}");
        }

        lines.Add($"    FROM {QualifiedName(sourceDatabase, sourceSchema, SelectedTableName)} AS {QuoteIdentifier(BaseAlias)}");
        foreach (var relationship in GetProjectionJoinDependencies(projectionSpecs))
        {
            foreach (var joinLine in BuildMergeJoinLines(relationship, sourceDatabase))
            {
                lines.Add($"    {joinLine}");
            }
        }

        if (!string.IsNullOrWhiteSpace(MergeDateFilterColumn))
        {
            lines.Add($"    WHERE {QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(MergeDateFilterColumn.Trim())} >= {BuildDateFilterThreshold(MergeDateFilterRange)}");
        }

        lines.Add(") AS src");

        var keyConditions = new List<string>
        {
            $"tgt.{QuoteIdentifier(SourceDbColumnName)} = src.{QuoteIdentifier(SourceDbColumnName)}"
        };
        keyConditions.AddRange(keySpecs.Select(spec => $"tgt.{QuoteIdentifier(spec.OutputColumnName)} = src.{QuoteIdentifier(spec.OutputColumnName)}"));

        // The date filter stays on the source WHERE only (it narrows to rows that MAY have changed);
        // the TS rowversion comparison in WHEN MATCHED picks the rows actually changed since last load.
        // It must NOT be added to the ON clause: a key whose target date fell outside the window would
        // fail the match and be re-INSERTed, causing a primary-key violation.

        var firstCondition = true;
        foreach (var condition in keyConditions)
        {
            lines.Add(firstCondition ? $"    ON {condition}" : $"        AND {condition}");
            firstCondition = false;
        }

        var updateSpecs = projectionSpecs.Where(spec => !spec.IsPrimaryKey).ToList();
        if (updateSpecs.Count > 0)
        {
            // With the TS column (default) only rows whose source rowversion is newer are updated;
            // without it every matched row is updated.
            lines.Add(tsSpec is not null
                ? $"WHEN MATCHED AND {MergeSourceValue(tsSpec)} > tgt.{QuoteIdentifier(DestColumnName(tsSpec))} THEN"
                : "WHEN MATCHED THEN");
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
        lines.Add($"{ProjectionPrefix(true)}{QuoteIdentifier(SourceDbColumnName)}");
        lines.Add($"{ProjectionPrefix(false)}{QuoteIdentifier(TableNameColumnName)}");
        foreach (var spec in projectionSpecs)
        {
            lines.Add($"{ProjectionPrefix(false)}{QuoteIdentifier(DestColumnName(spec))}");
        }

        lines.Add("    )");
        lines.Add("    VALUES");
        lines.Add("    (");
        lines.Add($"{ProjectionPrefix(true)}src.{QuoteIdentifier(SourceDbColumnName)}");
        lines.Add($"{ProjectionPrefix(false)}src.{QuoteIdentifier(TableNameColumnName)}");
        foreach (var spec in projectionSpecs)
        {
            lines.Add($"{ProjectionPrefix(false)}{MergeSourceValue(spec)}");
        }

        lines.Add("    );");

        return lines;
    }

    // Builds a self-contained T-SQL script that loops the VVG_Silver control table and runs one
    // dynamic MERGE per replicated source. The MERGE is emitted once as a template whose source
    // database appears as a sentinel token in two forms -- bracketed [__SOURCE_DB__] in the FROM/JOIN
    // identifiers and quoted '__SOURCE_DB__' as the [SourceDB] column value; the generated cursor
    // REPLACEs both with each row's ReplicatedDBName before EXEC sp_executesql.
    private List<string> BuildBatchMergeSql(IReadOnlyList<ProjectionSpec> projectionSpecs, out string unavailableReason)
    {
        if (!TryPrepareMergeSpecs(projectionSpecs, out var tsSpec, out var keySpecs, out unavailableReason))
        {
            return new List<string>();
        }

        var sourceSchema = string.IsNullOrWhiteSpace(MergeSourceSchema) ? SelectedSchemaName : MergeSourceSchema.Trim();

        // The source database becomes a sentinel token the loop replaces per row -- once as the
        // FROM/JOIN identifier and once as the quoted [SourceDB] column value.
        var templateLines = BuildMergeStatementLines(
            projectionSpecs,
            keySpecs,
            tsSpec,
            $"'{BatchSourceDbToken}'",
            BatchSourceDbToken,
            sourceSchema);

        // Embed the MERGE template in an nvarchar literal: every ' is doubled so the literal survives.
        var template = string.Join(Environment.NewLine, templateLines).Replace("'", "''");

        // QualifiedName emits the source token bracketed (e.g. [__SOURCE_DB__]); match that in REPLACE.
        var sourceDbTokenSql = QuoteIdentifier(BatchSourceDbToken);

        // Wrap the loop in a stored procedure named MergeExcede<OutputTableName> created in the
        // destination database/schema (e.g. [dbo].[MergeExcedeCOEMP]).
        var outputTableToken = SanitizeAliasToken(string.IsNullOrWhiteSpace(TargetTableName) ? TargetViewName : TargetTableName);
        var procName = QualifiedName(MergeDestinationSchema, $"MergeExcede{outputTableToken}");

        var lines = new List<string>
        {
            "-- =============================================================================",
            "-- Batch incremental MERGE across every replicated Excede source, wrapped as a stored proc.",
            "-- Loops VVG_Silver.dbo.ReplicatedExcedeSources and runs one MERGE per row, retargeting the",
            "-- source database for each ReplicatedDBName -- used both as the FROM/JOIN identifier and as",
            "-- the [SourceDB] column value. QUOTENAME guards the injected identifier.",
            "-- =============================================================================",
            $"CREATE OR ALTER PROCEDURE {procName}",
            "AS",
            "SET NOCOUNT ON;",
            "",
            "DECLARE @ReplicatedDB  sysname;",
            "DECLARE @sql           nvarchar(max);",
            "DECLARE @template      nvarchar(max) = N'",
            template,
            "';",
            "",
            "DECLARE source_cursor CURSOR LOCAL FAST_FORWARD FOR",
            "    SELECT ReplicatedDBName",
            "    FROM VVG_Silver.dbo.ReplicatedExcedeSources",
            "    ORDER BY ReplicatedDBName;",
            "",
            "OPEN source_cursor;",
            "FETCH NEXT FROM source_cursor INTO @ReplicatedDB;",
            "",
            "WHILE @@FETCH_STATUS = 0",
            "BEGIN",
            $"    SET @sql = REPLACE(@template, '{sourceDbTokenSql}', QUOTENAME(@ReplicatedDB));",
            $"    SET @sql = REPLACE(@sql, '''{BatchSourceDbToken}''', ''''+@ReplicatedDB+'''');",
            "",
            "    EXEC sys.sp_executesql @sql;",
            "",
            "    FETCH NEXT FROM source_cursor INTO @ReplicatedDB;",
            "END",
            "",
            "CLOSE source_cursor;",
            "DEALLOCATE source_cursor;"
        };

        return lines;
    }

    // A surrogate projection bakes the design-time source database as its leading literal. In a MERGE the
    // row's SourceDB is countryDbSourceExpression -- a quoted literal for a single target, or the
    // '__SOURCE_DB__' sentinel the batch cursor already REPLACEs per source -- so retarget the baked
    // literal to it. Non-surrogate projections carry no such literal, so this is a no-op for them.
    private string RetargetSurrogateSourceDb(string sourceProjection, string sourceDbExpression) =>
        string.IsNullOrWhiteSpace(SelectedDatabaseName)
            ? sourceProjection
            : sourceProjection.Replace(QuoteSqlLiteral(SelectedDatabaseName), sourceDbExpression, StringComparison.Ordinal);

    private static string MergeSourceValue(ProjectionSpec spec)
    {
        if (string.Equals(spec.OutputColumnName, RowVersionColumnName, StringComparison.OrdinalIgnoreCase))
        {
            return $"CONVERT(bigint, src.{QuoteIdentifier(spec.OutputColumnName)})";
        }

        return $"src.{QuoteIdentifier(spec.OutputColumnName)}";
    }

    // Relative lower-bound expression for the optional merge date filter, evaluated by SQL Server at run time.
    private static string BuildDateFilterThreshold(string range) => range switch
    {
        DateRange7 => "DATEADD(DAY, -7, CAST(GETDATE() AS date))",
        DateRange60 => "DATEADD(DAY, -60, CAST(GETDATE() AS date))",
        DateRangePriorMonth => "DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()) - 1, 0)",
        DateRangeCurrentMonth => "DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)",
        _ => "DATEADD(DAY, -30, CAST(GETDATE() AS date))" // DateRange30 (default)
    };

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

    private IEnumerable<string> BuildProjectionLines(IReadOnlyList<ProjectionSpec> projectionSpecs, bool leadingColumnsEmitted = false)
    {
        var first = !leadingColumnsEmitted;

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

    private IEnumerable<string> BuildControlProjectionLines()
    {
        yield return BuildProjectionLine(
            ProjectionPrefix(true),
            $"{QuoteSqlLiteral(SelectedDatabaseName)} AS {QuoteIdentifier(SourceDbColumnName)}",
            SourceDbColumnName,
            "Source DB",
            "Name of the source Database the row originated from.");
        yield return BuildProjectionLine(
            ProjectionPrefix(false),
            $"{QuoteSqlLiteral(SelectedTableName)} AS {QuoteIdentifier(TableNameColumnName)}",
            TableNameColumnName,
            "Source Table Name",
            "Name of the source Table the row originated from.");
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

    private IEnumerable<string> BuildCteSurfaceProjectionLines(string sourceCteName, IReadOnlyList<ProjectionSpec> projectionSpecs, bool leadingColumnsEmitted = false)
    {
        var first = !leadingColumnsEmitted;

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
            var (businessName, businessDescription) = ApplyImportedComment(column.ColumnName, column.BusinessName, column.BusinessDescription);
            yield return new ProjectionSpec(projection, column.ColumnName, businessName, businessDescription, false, false, null, UdtTypeResolver.ResolveByName(column.DataType), column.IsNullable, column.IsPrimaryKey);
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
                var (businessName, businessDescription) = ApplyImportedComment(fkOutputName, column?.BusinessName, column?.BusinessDescription);
                // A composite-PK column that is ALSO an FK is projected here as [col]_FK, so it must keep
                // its primary-key flag -- otherwise the CREATE TABLE PRIMARY KEY constraint and the MERGE
                // match keys would silently drop it and only include the non-FK key columns.
                yield return new ProjectionSpec(projection, fkOutputName, businessName, businessDescription, true, startsRelationshipGroup, null, UdtTypeResolver.ResolveByName(column?.DataType ?? ""), column?.IsNullable ?? true, column?.IsPrimaryKey ?? false);
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
            var (displayBusinessName, displayBusinessDescription) = ApplyImportedComment(columnAlias, relationship.DisplayBusinessName, relationship.DisplayBusinessDescription);
            yield return new ProjectionSpec(lookupProjection, columnAlias, displayBusinessName, displayBusinessDescription, true, false, relationship, GetDisplayColumnDataType(relationship), displayNullable, false);
        }

        // Surrogate keys trail the projection as one grouped block.
        var startsSurrogateGroup = true;
        foreach (var surrogate in BuildSurrogateKeyPlan())
        {
            var projection = $"{surrogate.Expression} AS {QuoteIdentifier(surrogate.OutputName)}";
            var (surrogateName, surrogateDescription) = ApplyImportedComment(surrogate.OutputName, surrogate.BusinessName, surrogate.BusinessDescription);
            yield return new ProjectionSpec(projection, surrogate.OutputName, surrogateName, surrogateDescription, true, startsSurrogateGroup, null, surrogate.SqlDataType, false, false);
            startsSurrogateGroup = false;
        }
    }

    // Builds the surrogate-key plan from the per-key definitions edited in the dialog: every included
    // key becomes one projected column. The expression is the operator's edited text when present,
    // otherwise the auto-derived [SourceDB]+columns concatenation. Names de-dup against the projection.
    private IReadOnlyList<SurrogateKeyPlanItem> BuildSurrogateKeyPlan()
    {
        var items = new List<SurrogateKeyPlanItem>();
        var usedNames = new HashSet<string>(BuildProjectionOutputColumnNames(), StringComparer.OrdinalIgnoreCase);

        string UniqueName(string preferred)
        {
            var candidate = string.IsNullOrWhiteSpace(preferred) ? $"Surrogate{SurrogateKeySuffix}" : preferred.Trim();
            var name = candidate;
            var suffix = 2;
            while (!usedNames.Add(name))
            {
                name = $"{candidate}_{suffix}";
                suffix++;
            }

            return name;
        }

        foreach (var definition in SurrogateKeys)
        {
            if (!definition.Include)
            {
                continue;
            }

            var columns = definition.ColumnNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            var expression = string.IsNullOrWhiteSpace(definition.Expression)
                ? BuildSurrogateKeyExpressionForColumns(columns)
                : definition.Expression.Trim();

            if (columns.Count == 0 || string.IsNullOrWhiteSpace(definition.OutputName) || string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            var length = definition.Length > 0 ? definition.Length : 400;
            var sqlType = string.Equals(definition.SqlType, "varchar", StringComparison.OrdinalIgnoreCase) ? "varchar" : "nvarchar";
            var description = string.IsNullOrWhiteSpace(definition.BusinessDescription)
                ? $"Single-column Power BI join key built from [SourceDB] plus ({string.Join(", ", columns)})."
                : definition.BusinessDescription.Trim();
            items.Add(new SurrogateKeyPlanItem(
                UniqueName(definition.OutputName),
                expression,
                $"{sqlType}({length})",
                "PBI Surrogate Key",
                description));
        }

        return items;
    }

    // A surrogate is CONCAT([SourceDB] literal, delimiter, part, delimiter, part, ...). Only NUMERIC
    // columns are CAST (to a right-sized varchar) so the value is deterministic and the output column can
    // be sized exactly; string columns pass through uncast (CONCAT converts them anyway). CONCAT coalesces
    // NULLs to '' so the result is never NULL, and the delimiter guards against boundary collisions.
    private string BuildSurrogateKeyExpressionForColumns(IReadOnlyList<string> columnNames)
    {
        var parts = new List<string> { QuoteSqlLiteral(SelectedDatabaseName) };
        foreach (var columnName in columnNames)
        {
            parts.Add(QuoteSqlLiteral(SurrogateKeyDelimiter));
            parts.Add(BuildSurrogateKeyPart(columnName).Expression);
        }

        return $"CONCAT({string.Join(", ", parts)})";
    }

    // Default output length for a surrogate column: the [SourceDB] width plus, per key column, one
    // delimiter and that column's rendered width. Seeded as the definition's Length (operator can override).
    private int BuildSurrogateKeyLengthForColumns(IReadOnlyList<string> columnNames)
    {
        var length = ParseSqlTypeLength(SourceDbColumnType) ?? 128;
        foreach (var columnName in columnNames)
        {
            length += SurrogateKeyDelimiter.Length + BuildSurrogateKeyPart(columnName).Length;
        }

        return length;
    }

    // One surrogate key part. Numeric source columns are CAST to a right-sized varchar; everything else
    // (strings, dates, guids) passes through uncast. Length is the part's worst-case character width.
    private (string Expression, int Length) BuildSurrogateKeyPart(string columnName)
    {
        var qualified = QualifyBaseColumnRef(columnName);
        var column = Columns.FirstOrDefault(c => string.Equals(c.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
        var (isNumeric, length) = DescribeSurrogateType(UdtTypeResolver.ResolveByName(column?.DataType ?? ""));
        return isNumeric
            ? ($"CAST({qualified} AS varchar({length}))", length)
            : (qualified, length);
    }

    // Classifies a resolved SQL type for surrogate purposes: whether it is numeric (needs a CAST) and its
    // worst-case character width, derived from the source column's own type.
    private static (bool IsNumeric, int Length) DescribeSurrogateType(string resolvedType)
    {
        var lower = (resolvedType ?? string.Empty).Trim().ToLowerInvariant();
        var baseName = lower;
        string? firstArg = null;
        var paren = lower.IndexOf('(');
        if (paren >= 0)
        {
            baseName = lower.Substring(0, paren).Trim();
            firstArg = lower.Substring(paren + 1).TrimEnd(')').Split(',')[0].Trim();
        }

        switch (baseName)
        {
            case "bit": return (true, 1);
            case "tinyint": return (true, 3);
            case "smallint": return (true, 6);
            case "int": return (true, 11);
            case "bigint": return (true, 20);
            case "smallmoney": return (true, 12);
            case "money": return (true, 21);
            case "real":
            case "float": return (true, 25);
            case "decimal":
            case "numeric":
            {
                var precision = int.TryParse(firstArg, out var p) ? p : 18;
                var scaled = lower.Contains(',') && !lower.TrimEnd(')').EndsWith(",0", StringComparison.Ordinal);
                return (true, precision + (scaled ? 1 : 0));
            }
            case "char":
            case "nchar":
            case "varchar":
            case "nvarchar":
                return (false, int.TryParse(firstArg, out var n) ? n : 128);
            case "sysname": return (false, 128);
            case "uniqueidentifier": return (false, 36);
            case "date": return (false, 10);
            case "time": return (false, 16);
            case "smalldatetime": return (false, 19);
            case "datetime": return (false, 23);
            case "datetime2": return (false, 27);
            case "datetimeoffset": return (false, 34);
            default: return (false, 128);
        }
    }

    // Parses the (n) length from a type string such as "nvarchar(128)"; null for types without one.
    private static int? ParseSqlTypeLength(string sqlType)
    {
        var open = sqlType.IndexOf('(');
        if (open < 0)
        {
            return null;
        }

        var inner = sqlType.Substring(open + 1).TrimEnd(')').Split(',')[0].Trim();
        return int.TryParse(inner, out var length) ? length : (int?)null;
    }

    private string QualifyBaseColumnRef(string columnName) =>
        $"{QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(columnName)}";

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
        foreach (var name in BuildProjectionOutputColumnNames())
        {
            yield return name;
        }

        foreach (var surrogate in BuildSurrogateKeyPlan())
        {
            yield return surrogate.OutputName;
        }
    }

    private IEnumerable<string> BuildProjectionOutputColumnNames()
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

    private sealed record SurrogateKeyPlanItem(
        string OutputName,
        string Expression,
        string SqlDataType,
        string BusinessName,
        string BusinessDescription);
}
