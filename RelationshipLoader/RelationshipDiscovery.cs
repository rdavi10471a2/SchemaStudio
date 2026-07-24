using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.Data.Models;

namespace RelationshipLoader;

// Reads foreign keys from the source (Excede) database and turns each FK into the SAME two
// registry rows the Base View Creator / relationship editor produce on "Load From Source":
//   SchemaLookup : source = child (FK holder), target = referenced parent   (the child's lookup)
//   SchemaChild  : source = referenced parent, target = child               (the parent's child)
// Only physical FKs are read here; soft/Manual relationships (e.g. Status) are left to the app.
public sealed class RelationshipDiscovery
{
    private readonly string _sourceConnectionString;
    private readonly TableDisplayColumnPolicy _policy = new();

    // COLOOKUP generic-lookup shape (mirrors DatabaseRelationshipsPanel): one shared code table keyed
    // by Id, discriminated by a Name category, with DES1 as the display value.
    private const string ColookupTargetSchema = "dbo";
    private const string ColookupTargetTable = "COLOOKUP";
    private const string ColookupKeyColumn = "Id";
    private const string ColookupFilterColumn = "Name";
    private const string ColookupDisplayColumn = "DES1";

    public RelationshipDiscovery(string sourceConnectionString) => _sourceConnectionString = sourceConnectionString;

    // One row per FK column pair; grouped by FK in memory.
    private sealed class FkColumnRow
    {
        public int FkObjectId { get; set; }
        public string ForeignKeyName { get; set; } = "";
        public string ChildSchema { get; set; } = "";
        public string ChildTable { get; set; } = "";
        public string ChildColumn { get; set; } = "";
        public bool ChildColumnIsNullable { get; set; }
        public string ParentSchema { get; set; } = "";
        public string ParentTable { get; set; } = "";
        public string ParentColumn { get; set; } = "";
        public int KeyOrdinal { get; set; }
    }

    private sealed class ColumnRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
    }

    private const string FkSql = """
        SELECT
            fk.object_id             AS FkObjectId,
            fk.name                  AS ForeignKeyName,
            cs.name                  AS ChildSchema,
            ct.name                  AS ChildTable,
            cc.name                  AS ChildColumn,
            cc.is_nullable           AS ChildColumnIsNullable,
            ps.name                  AS ParentSchema,
            pt.name                  AS ParentTable,
            pc.name                  AS ParentColumn,
            fkc.constraint_column_id AS KeyOrdinal
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.tables  AS ct ON ct.object_id = fk.parent_object_id
        JOIN sys.schemas AS cs ON cs.schema_id = ct.schema_id
        JOIN sys.columns AS cc ON cc.object_id = fkc.parent_object_id     AND cc.column_id = fkc.parent_column_id
        JOIN sys.tables  AS pt ON pt.object_id = fk.referenced_object_id
        JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
        JOIN sys.columns AS pc ON pc.object_id = fkc.referenced_object_id AND pc.column_id = fkc.referenced_column_id
        ORDER BY fk.name, fkc.constraint_column_id;
        """;

    private const string ColumnsSql = """
        SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName
        FROM sys.columns AS c
        JOIN sys.tables  AS t ON t.object_id = c.object_id
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id;
        """;

    public async Task<List<DatabaseRelationshipDefinition>> DiscoverAsync(
        int databaseId,
        string sourceDatabaseName,
        bool includeLookups,
        bool includeChildren,
        bool includeColookupLookups,
        string? sqlLookupTemplate,
        bool ignoreSelfJoins,
        Action<string> log)
    {
        // Read schema from the SELECTED database, whatever InitialCatalog the base connection carries.
        // The source app targets DBs by name over one server connection; we mirror that by pointing
        // InitialCatalog at the chosen database so the unqualified sys.* queries hit the right catalog.
        var effectiveConnectionString = new SqlConnectionStringBuilder(_sourceConnectionString)
        {
            InitialCatalog = sourceDatabaseName,
        }.ConnectionString;

        await using var connection = new SqlConnection(effectiveConnectionString);
        await connection.OpenAsync();
        log($"Reading schema from catalog [{sourceDatabaseName}].");

        var fkRows = (await connection.QueryAsync<FkColumnRow>(FkSql)).ToList();
        log($"Read {fkRows.Count} FK column row(s) from source.");

        var columnsByTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in await connection.QueryAsync<ColumnRow>(ColumnsSql))
        {
            var key = $"{col.SchemaName}.{col.TableName}";
            if (!columnsByTable.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                columnsByTable[key] = set;
            }

            set.Add(col.ColumnName);
        }

        string? PickDisplayColumn(string schema, string table)
        {
            if (!columnsByTable.TryGetValue($"{schema}.{table}", out var available))
            {
                return null;
            }

            foreach (var preferred in _policy.GetPreferredDisplayColumns(sourceDatabaseName, schema, table))
            {
                if (available.Contains(preferred))
                {
                    return preferred;
                }
            }

            return null;
        }

        var results = new List<DatabaseRelationshipDefinition>();
        var fkGroups = fkRows.GroupBy(row => row.FkObjectId).ToList();
        var skippedSelfJoins = 0;

        foreach (var group in fkGroups)
        {
            var first = group.First();

            if (ignoreSelfJoins &&
                string.Equals(first.ChildSchema, first.ParentSchema, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(first.ChildTable, first.ParentTable, StringComparison.OrdinalIgnoreCase))
            {
                skippedSelfJoins++;
                continue;
            }

            var pairs = group.OrderBy(row => row.KeyOrdinal).ToList();

            if (includeLookups)
            {
                results.Add(BuildLookup(databaseId, first, pairs, PickDisplayColumn(first.ParentSchema, first.ParentTable)));
            }

            if (includeChildren)
            {
                results.Add(BuildChild(databaseId, first, pairs));
            }
        }

        log($"Discovered {fkGroups.Count} foreign key(s)" +
            (skippedSelfJoins > 0 ? $" (skipped {skippedSelfJoins} self-join(s))" : "") +
            $" -> {results.Count} FK relationship row(s).");

        if (includeColookupLookups && !string.IsNullOrWhiteSpace(sqlLookupTemplate))
        {
            var colookupStaged = await DiscoverColookupLookupsAsync(
                connection, databaseId, columnsByTable, results, log);
            log($"Discovered {colookupStaged} COLOOKUP lookup(s) via the lookup query template.");
        }

        log($"Total: {results.Count} relationship row(s) to upsert.");
        return results;
    }

    // COLOOKUP generic-lookup discovery, done as a bulk correlation of two in-memory lists:
    //   1. the SCHEMA list — every table and its columns (columnsByTable, already read above), and
    //   2. the SOFT list   — every category Name in the shared COLOOKUP table, read here in ONE query.
    // We walk the schema list table-by-table and, for each of that table's columns, correlate against
    // the soft list: wherever the category "{table}_{column}" (e.g. COEMP_ROLE) exists, we emit a
    // filtered lookup into the shared COLOOKUP table for that column.
    //
    // This is schema-DRIVEN on purpose. The old code drove off the Name list and GUESSED the owning
    // table by longest-prefix match, which mis-split names when table names share prefixes
    // (COEMP vs COEMP_EXT) and could invent a column the table never had. Correlating known
    // table+column against the soft list as a membership set removes both failure modes.
    // (The SQLLookupString template filters on [table], so running it with an empty table token
    // returns nothing; reading COLOOKUP directly is the bulk preload.)
    private async Task<int> DiscoverColookupLookupsAsync(
        SqlConnection connection,
        int databaseId,
        Dictionary<string, HashSet<string>> columnsByTable,
        List<DatabaseRelationshipDefinition> results,
        Action<string> log)
    {
        var lookupSql =
            $"SELECT DISTINCT [{ColookupFilterColumn}] AS Name " +
            $"FROM [{ColookupTargetSchema}].[{ColookupTargetTable}] " +
            $"WHERE [{ColookupFilterColumn}] IS NOT NULL;";
        log($"  Lookup query: {lookupSql}");

        // Soft list keyed by category name (case-insensitive) -> the name AS STORED, so the emitted
        // FilterValue matches the real COLOOKUP data's casing rather than our reconstructed key.
        Dictionary<string, string> softNames;
        try
        {
            softNames = (await connection.QueryAsync<string>(lookupSql))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            log($"  COLOOKUP query failed (does [{ColookupTargetSchema}].[{ColookupTargetTable}] exist?): {ex.Message}");
            return 0;
        }

        log($"  COLOOKUP returned {softNames.Count} category name(s).");

        var staged = 0;
        foreach (var tableKey in columnsByTable.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            // columnsByTable is keyed "{schema}.{table}"; the COLOOKUP category uses the bare table.
            var dot = tableKey.IndexOf('.');
            var schema = dot >= 0 ? tableKey[..dot] : ColookupTargetSchema;
            var table = dot >= 0 ? tableKey[(dot + 1)..] : tableKey;

            foreach (var column in columnsByTable[tableKey].OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (softNames.TryGetValue($"{table}_{column}", out var categoryName))
                {
                    results.Add(BuildColookupLookupRelationship(databaseId, schema, table, column, categoryName));
                    staged++;
                }
            }
        }

        return staged;
    }

    private static DatabaseRelationshipDefinition BuildColookupLookupRelationship(
        int databaseId,
        string sourceSchema,
        string sourceTable,
        string sourceColumn,
        string lookupName)
    {
        return new DatabaseRelationshipDefinition
        {
            DatabaseId = databaseId,
            SourceSchemaName = sourceSchema,
            SourceTableName = sourceTable,
            TargetSchemaName = ColookupTargetSchema,
            TargetTableName = ColookupTargetTable,
            JoinType = RelationshipJoinPolicy.LeftJoin,
            DiscoverySource = "SchemaLookup",
            SourceConstraintName = Truncate($"LOOKUP_{ColookupTargetTable}_{sourceTable}_{sourceColumn}"),
            JoinExpression = $"[{sourceTable}].[{sourceColumn}] = [{ColookupTargetTable}].[{ColookupKeyColumn}]",
            DisplayColumnName = ColookupDisplayColumn,
            FilterColumnName = ColookupFilterColumn,
            FilterValue = Truncate(lookupName),
            IncludeLookupByDefault = true,
            Columns =
            [
                new DatabaseRelationshipColumnDefinition
                {
                    OrdinalPosition = 1,
                    SourceColumnName = sourceColumn,
                    TargetColumnName = ColookupKeyColumn,
                },
            ],
        };
    }

    private static string Truncate(string value) => value.Length <= 128 ? value : value[..128];

    // SchemaLookup: focus is the child; it looks up the referenced parent.
    private static DatabaseRelationshipDefinition BuildLookup(
        int databaseId,
        FkColumnRow first,
        List<FkColumnRow> pairs,
        string? displayColumn)
    {
        return new DatabaseRelationshipDefinition
        {
            DatabaseId = databaseId,
            SourceSchemaName = first.ChildSchema,
            SourceTableName = first.ChildTable,
            TargetSchemaName = first.ParentSchema,
            TargetTableName = first.ParentTable,
            // Shared rule with the Relationships Dialog (RelationshipJoinPolicy): an FK lookup gets an
            // INNER JOIN only when every child (many-side) FK column is non-nullable; otherwise LEFT JOIN.
            JoinType = RelationshipJoinPolicy.ForLookup(pairs.Select(p => p.ChildColumnIsNullable)),
            DiscoverySource = "SchemaLookup",
            SourceConstraintName = first.ForeignKeyName,
            JoinExpression = string.Join(
                " AND ",
                pairs.Select(p => $"[{first.ChildTable}].[{p.ChildColumn}] = [{first.ParentTable}].[{p.ParentColumn}]")),
            DisplayColumnName = displayColumn,
            IncludeLookupByDefault = displayColumn is not null,
            Columns = pairs
                .Select((p, index) => new DatabaseRelationshipColumnDefinition
                {
                    OrdinalPosition = index + 1,
                    SourceColumnName = p.ChildColumn,
                    TargetColumnName = p.ParentColumn,
                })
                .ToList(),
        };
    }

    // SchemaChild: focus is the referenced parent; it lists the child that references it.
    private static DatabaseRelationshipDefinition BuildChild(
        int databaseId,
        FkColumnRow first,
        List<FkColumnRow> pairs)
    {
        return new DatabaseRelationshipDefinition
        {
            DatabaseId = databaseId,
            SourceSchemaName = first.ParentSchema,
            SourceTableName = first.ParentTable,
            TargetSchemaName = first.ChildSchema,
            TargetTableName = first.ChildTable,
            JoinType = RelationshipJoinPolicy.LeftJoin,
            DiscoverySource = "SchemaChild",
            SourceConstraintName = first.ForeignKeyName,
            JoinExpression = string.Join(
                " AND ",
                pairs.Select(p => $"[{first.ParentTable}].[{p.ParentColumn}] = [{first.ChildTable}].[{p.ChildColumn}]")),
            IncludeLookupByDefault = false,
            Columns = pairs
                .Select((p, index) => new DatabaseRelationshipColumnDefinition
                {
                    OrdinalPosition = index + 1,
                    SourceColumnName = p.ParentColumn,
                    TargetColumnName = p.ChildColumn,
                })
                .ToList(),
        };
    }
}
