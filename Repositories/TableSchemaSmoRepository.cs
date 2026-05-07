using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Data;

[FileVersion("1.2")]
[AIFileContext("Repositories/TableSchemaSmoRepository.cs", "Reads SQL Server table metadata for the Base View Generator page.", Responsibilities = "Provides schema, table, column, and many-to-one foreign-key metadata from a selected source database without changing the configured connection string.", Nuances = "The class name is retained from the first SMO implementation, but the metadata reads use targeted sys catalog queries because SMO object hydration was too slow for interactive use.", LastReviewed = "2026-05-07")]
public sealed class TableSchemaSmoRepository
{
    private readonly string connectionString;

    public TableSchemaSmoRepository(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task<IReadOnlyList<string>> GetSchemasAsync(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        var database = QuoteSqlIdentifier(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var sql = $"""
SELECT
    s.name
FROM {database}.sys.schemas AS s
WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
ORDER BY s.name;
""";

        var schemas = await connection.QueryAsync<string>(sql);
        return schemas.ToList();
    }

    public async Task<IReadOnlyList<TableSchemaTableInfo>> GetTablesAsync(string databaseName, string schemaName)
    {
        ValidateDatabaseName(databaseName);
        var database = QuoteSqlIdentifier(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var sql = $"""
SELECT
    s.name AS SchemaName,
    t.name AS TableName
FROM {database}.sys.tables AS t
JOIN {database}.sys.schemas AS s
    ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
    AND s.name = @schemaName
ORDER BY t.name;
""";

        var tables = await connection.QueryAsync<TableSchemaTableInfo>(sql, new { schemaName });
        return tables.ToList();
    }

    public async Task<TableSchemaDetails> GetTableDetailsAsync(string databaseName, string schemaName, string tableName)
    {
        ValidateDatabaseName(databaseName);
        var database = QuoteSqlIdentifier(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await EnsureDatabaseExistsAsync(connection, databaseName);

        var tableObjectId = await GetTableObjectIdAsync(connection, database, databaseName, schemaName, tableName);
        var columns = (await connection.QueryAsync<TableSchemaColumnRow>(BuildColumnsSql(database), new { tableObjectId }))
            .Select(column => new TableSchemaColumnInfo(
                column.ColumnName,
                column.DataType,
                column.IsNullable,
                column.IsPrimaryKey,
                false))
            .ToList();

        var columnByName = columns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        var relationshipRows = (await connection.QueryAsync<TableSchemaRelationshipRow>(BuildRelationshipsSql(database), new { tableObjectId })).ToList();
        var relationships = new List<TableSchemaRelationshipInfo>();

        foreach (var group in relationshipRows.GroupBy(row => new
                 {
                     row.ForeignKeyName,
                     row.ReferencedSchemaName,
                     row.ReferencedTableName,
                     row.DisplayColumnName
                 }))
        {
            var pairs = group
                .OrderBy(row => row.ConstraintColumnId)
                .Select(row =>
                {
                    if (columnByName.TryGetValue(row.LocalColumnName, out var localColumn))
                    {
                        localColumn.IsForeignKey = true;
                    }

                    return new TableSchemaForeignKeyColumnInfo(row.LocalColumnName, row.ReferencedColumnName);
                })
                .ToList();

            var isRequired = pairs.Count > 0 &&
                pairs.All(pair => columnByName.TryGetValue(pair.LocalColumnName, out var localColumn) && !localColumn.IsNullable);

            relationships.Add(new TableSchemaRelationshipInfo(
                group.Key.ForeignKeyName,
                group.Key.ReferencedSchemaName,
                group.Key.ReferencedTableName,
                group.Key.DisplayColumnName,
                isRequired,
                isRequired ? "INNER JOIN" : "LEFT JOIN",
                pairs));
        }

        return new TableSchemaDetails(databaseName, schemaName, tableName, columns, relationships);
    }

    private static async Task EnsureDatabaseExistsAsync(SqlConnection connection, string databaseName)
    {
        const string sql = "SELECT 1 FROM sys.databases WHERE name = @databaseName;";
        var exists = await connection.ExecuteScalarAsync<int?>(sql, new { databaseName });
        if (exists != 1)
        {
            throw new InvalidOperationException($"Database [{databaseName}] was not found.");
        }
    }

    private static async Task<int> GetTableObjectIdAsync(SqlConnection connection, string database, string databaseName, string schemaName, string tableName)
    {
        var sql = $"""
SELECT t.object_id
FROM {database}.sys.tables AS t
JOIN {database}.sys.schemas AS s
    ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
    AND s.name = @schemaName
    AND t.name = @tableName;
""";

        var objectId = await connection.ExecuteScalarAsync<int?>(sql, new { schemaName, tableName });
        if (objectId is null)
        {
            throw new InvalidOperationException($"Table [{databaseName}].[{schemaName}].[{tableName}] was not found.");
        }

        return objectId.Value;
    }

    private static string BuildColumnsSql(string database)
    {
        return $"""
SELECT
    c.name AS ColumnName,
    CASE
        WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary')
            THEN CONCAT(ty.name, '(', CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length) END, ')')
        WHEN ty.name IN ('nvarchar', 'nchar')
            THEN CONCAT(ty.name, '(', CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length / 2) END, ')')
        WHEN ty.name IN ('decimal', 'numeric')
            THEN CONCAT(ty.name, '(', c.precision, ',', c.scale, ')')
        WHEN ty.name IN ('datetime2', 'datetimeoffset', 'time')
            THEN CONCAT(ty.name, '(', c.scale, ')')
        ELSE ty.name
    END AS DataType,
    CONVERT(bit, c.is_nullable) AS IsNullable,
    CONVERT(bit, CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END) AS IsPrimaryKey
FROM {database}.sys.columns AS c
JOIN {database}.sys.types AS ty
    ON ty.user_type_id = c.user_type_id
OUTER APPLY
(
    SELECT TOP (1) ic.column_id
    FROM {database}.sys.indexes AS i
    JOIN {database}.sys.index_columns AS ic
        ON ic.object_id = i.object_id
        AND ic.index_id = i.index_id
    WHERE i.object_id = c.object_id
        AND i.is_primary_key = 1
        AND ic.column_id = c.column_id
) AS pk
WHERE c.object_id = @tableObjectId
ORDER BY c.column_id;
""";
    }

    private static string BuildRelationshipsSql(string database)
    {
        return $"""
WITH DisplayColumns AS
(
    SELECT
        c.object_id,
        c.name AS DisplayColumnName,
        ROW_NUMBER() OVER
        (
            PARTITION BY c.object_id
            ORDER BY CASE c.name
                WHEN 'Des' THEN 1
                WHEN 'Des1' THEN 2
                WHEN 'Name' THEN 3
                WHEN 'Description' THEN 4
                WHEN 'Desc' THEN 5
                WHEN 'Title' THEN 6
                ELSE 99
            END
        ) AS DisplayRank
    FROM {database}.sys.columns AS c
    WHERE c.name IN ('Des', 'Des1', 'Name', 'Description', 'Desc', 'Title')
)
SELECT
    fk.name AS ForeignKeyName,
    rs.name AS ReferencedSchemaName,
    rt.name AS ReferencedTableName,
    dc.DisplayColumnName,
    fkc.constraint_column_id AS ConstraintColumnId,
    pc.name AS LocalColumnName,
    rc.name AS ReferencedColumnName
FROM {database}.sys.foreign_keys AS fk
JOIN {database}.sys.foreign_key_columns AS fkc
    ON fkc.constraint_object_id = fk.object_id
JOIN {database}.sys.columns AS pc
    ON pc.object_id = fkc.parent_object_id
    AND pc.column_id = fkc.parent_column_id
JOIN {database}.sys.tables AS rt
    ON rt.object_id = fk.referenced_object_id
JOIN {database}.sys.schemas AS rs
    ON rs.schema_id = rt.schema_id
JOIN {database}.sys.columns AS rc
    ON rc.object_id = fkc.referenced_object_id
    AND rc.column_id = fkc.referenced_column_id
LEFT JOIN DisplayColumns AS dc
    ON dc.object_id = rt.object_id
    AND dc.DisplayRank = 1
WHERE fk.parent_object_id = @tableObjectId
ORDER BY fk.name, fkc.constraint_column_id;
""";
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }
    }

    private static string QuoteSqlIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private sealed class TableSchemaColumnRow
    {
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
    }

    private sealed class TableSchemaRelationshipRow
    {
        public string ForeignKeyName { get; set; } = "";
        public string ReferencedSchemaName { get; set; } = "";
        public string ReferencedTableName { get; set; } = "";
        public string? DisplayColumnName { get; set; }
        public int ConstraintColumnId { get; set; }
        public string LocalColumnName { get; set; } = "";
        public string ReferencedColumnName { get; set; } = "";
    }
}

public sealed record TableSchemaTableInfo(string SchemaName, string TableName)
{
    public string DisplayName => $"{SchemaName}.{TableName}";
}

public sealed record TableSchemaDetails(
    string DatabaseName,
    string SchemaName,
    string TableName,
    IReadOnlyList<TableSchemaColumnInfo> Columns,
    IReadOnlyList<TableSchemaRelationshipInfo> Relationships);

public sealed class TableSchemaColumnInfo
{
    public TableSchemaColumnInfo(string columnName, string dataType, bool isNullable, bool isPrimaryKey, bool isForeignKey)
    {
        ColumnName = columnName;
        DataType = dataType;
        IsNullable = isNullable;
        IsPrimaryKey = isPrimaryKey;
        IsForeignKey = isForeignKey;
        Include = true;
    }

    public string ColumnName { get; }
    public string DataType { get; }
    public bool IsNullable { get; }
    public bool IsPrimaryKey { get; }
    public bool IsForeignKey { get; set; }
    public bool Include { get; set; }

    public string KeyRole =>
        IsPrimaryKey ? "PK" :
        IsForeignKey ? "FK M-to-1" :
        string.Empty;
}

public sealed class TableSchemaRelationshipInfo
{
    public TableSchemaRelationshipInfo(
        string foreignKeyName,
        string referencedSchemaName,
        string referencedTableName,
        string? displayColumnName,
        bool isRequired,
        string selectedJoinType,
        IReadOnlyList<TableSchemaForeignKeyColumnInfo> columns)
    {
        ForeignKeyName = foreignKeyName;
        ReferencedSchemaName = referencedSchemaName;
        ReferencedTableName = referencedTableName;
        DisplayColumnName = displayColumnName;
        IsRequired = isRequired;
        SelectedJoinType = selectedJoinType;
        Columns = columns;
        Include = true;
        IncludeDisplayColumn = !string.IsNullOrWhiteSpace(displayColumnName);
    }

    public string ForeignKeyName { get; }
    public string ReferencedSchemaName { get; }
    public string ReferencedTableName { get; }
    public string? DisplayColumnName { get; set; }
    public bool IsRequired { get; }
    public string SelectedJoinType { get; set; }
    public IReadOnlyList<TableSchemaForeignKeyColumnInfo> Columns { get; }
    public bool Include { get; set; }
    public bool IncludeDisplayColumn { get; set; }

    public string LocalColumns => string.Join(", ", Columns.Select(column => column.LocalColumnName));
    public string ReferencedColumns => string.Join(", ", Columns.Select(column => column.ReferencedColumnName));
}

public sealed record TableSchemaForeignKeyColumnInfo(string LocalColumnName, string ReferencedColumnName);
