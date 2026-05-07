using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Data;

[FileVersion("1.1")]
[AIFileContext("Repositories/TableSchemaSmoRepository.cs", "Reads SQL Server table metadata through SMO for the Base View Generator page.", Responsibilities = "Provides schema, table, column, and many-to-one foreign-key metadata from a selected source database without changing the configured connection string.", Nuances = "The connection string can point at the application/default database; SMO navigates to the selected source database through Server.Databases.", LastReviewed = "2026-05-07")]
public sealed class TableSchemaSmoRepository
{
    private readonly string connectionString;

    public TableSchemaSmoRepository(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public Task<IReadOnlyList<string>> GetSchemasAsync(string databaseName)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var database = GetDatabase(databaseName);
            database.Schemas.Refresh();

            return database.Schemas
                .Cast<Schema>()
                .Where(schema => !schema.IsSystemObject)
                .Select(schema => schema.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public Task<IReadOnlyList<TableSchemaTableInfo>> GetTablesAsync(string databaseName, string schemaName)
    {
        return Task.Run<IReadOnlyList<TableSchemaTableInfo>>(() =>
        {
            var database = GetDatabase(databaseName);
            database.Tables.Refresh();

            return database.Tables
                .Cast<Table>()
                .Where(table => !table.IsSystemObject)
                .Where(table => string.Equals(table.Schema, schemaName, StringComparison.OrdinalIgnoreCase))
                .Select(table => new TableSchemaTableInfo(table.Schema, table.Name))
                .OrderBy(table => table.TableName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public Task<TableSchemaDetails> GetTableDetailsAsync(string databaseName, string schemaName, string tableName)
    {
        return Task.Run(() =>
        {
            var database = GetDatabase(databaseName);
            var table = database.Tables[tableName, schemaName]
                ?? throw new InvalidOperationException($"Table [{databaseName}].[{schemaName}].[{tableName}] was not found.");

            table.Columns.Refresh();
            table.ForeignKeys.Refresh();

            var columns = table.Columns
                .Cast<Column>()
                .Select(column => new TableSchemaColumnInfo(
                    column.Name,
                    column.DataType?.SqlDataType.ToString() ?? column.DataType?.Name ?? string.Empty,
                    column.Nullable,
                    column.InPrimaryKey,
                    false))
                .ToList();

            var columnByName = columns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            var relationships = new List<TableSchemaRelationshipInfo>();

            foreach (var foreignKey in table.ForeignKeys.Cast<ForeignKey>().OrderBy(fk => fk.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(foreignKey.ReferencedTable, table.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(foreignKey.ReferencedTableSchema, table.Schema, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var referencedTable = database.Tables[foreignKey.ReferencedTable, foreignKey.ReferencedTableSchema];
                var referencedDisplayColumn = referencedTable == null
                    ? null
                    : PickDisplayColumn(referencedTable);

                var columnPairs = foreignKey.Columns
                    .Cast<ForeignKeyColumn>()
                    .Select(column =>
                    {
                        if (columnByName.TryGetValue(column.Name, out var localColumn))
                        {
                            localColumn.IsForeignKey = true;
                        }

                        return new TableSchemaForeignKeyColumnInfo(column.Name, column.ReferencedColumn);
                    })
                    .ToList();

                var allLocalColumnsRequired = columnPairs.Count > 0 &&
                    columnPairs.All(pair => columnByName.TryGetValue(pair.LocalColumnName, out var localColumn) && !localColumn.IsNullable);

                relationships.Add(new TableSchemaRelationshipInfo(
                    foreignKey.Name,
                    foreignKey.ReferencedTableSchema,
                    foreignKey.ReferencedTable,
                    referencedDisplayColumn,
                    allLocalColumnsRequired,
                    allLocalColumnsRequired ? "INNER JOIN" : "LEFT JOIN",
                    columnPairs));
            }

            return new TableSchemaDetails(databaseName, schemaName, tableName, columns, relationships);
        });
    }

    private Database GetDatabase(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }

        var sqlConnection = new SqlConnection(connectionString);
        var serverConnection = new ServerConnection(sqlConnection);
        var server = new Server(serverConnection);
        var database = server.Databases[databaseName];
        if (database == null)
        {
            throw new InvalidOperationException($"Database [{databaseName}] was not found.");
        }

        return database;
    }

    private static string? PickDisplayColumn(Table table)
    {
        table.Columns.Refresh();

        var candidates = new[]
        {
            "Des",
            "Des1",
            "Name",
            "Description",
            "Desc",
            "Title"
        };

        foreach (var candidate in candidates)
        {
            if (table.Columns.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
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
