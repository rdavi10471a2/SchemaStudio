using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

[FileVersion("1.3")]
[AIFileContext("SchemaStudio.Data/Repositories/DatabaseRelationshipRepository.cs", "Read/write repository for the curated database relationship registry.", Responsibilities = "Loads relationship headers with ordered column pairs and saves imported or user-curated relationships without creating or altering database objects.", Nuances = "This repository intentionally assumes dbo.DatabaseRelationships and dbo.DatabaseRelationshipColumns already exist; table creation remains a human-run script.", LastReviewed = "2026-05-13")]
public sealed class DatabaseRelationshipRepository
{
    private readonly string _connectionString;
    private readonly string _metadataDatabaseName;

    public DatabaseRelationshipRepository(string connectionString)
    {
        _connectionString = connectionString;
        _metadataDatabaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }

    public string MetadataDatabaseName => _metadataDatabaseName;

    private string RelationshipsTable => $"{QuoteSqlIdentifier(_metadataDatabaseName)}.dbo.DatabaseRelationships";

    private string RelationshipColumnsTable => $"{QuoteSqlIdentifier(_metadataDatabaseName)}.dbo.DatabaseRelationshipColumns";

    public async Task<IReadOnlyList<DatabaseRelationshipDefinition>> GetForDatabaseAsync(int databaseId)
    {
        await using var connection = new SqlConnection(_connectionString);

        var sql = $"""
SELECT
    DatabaseRelationshipId,
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    RelationshipRole,
    RelationshipName,
    RelationshipKey,
    SourceSystemDetected,
    UserConfirmed,
    DefaultIncludeInBaseView,
    UseInDomainObjectModeler,
    UseInQueryBuilder,
    DisplayColumnName,
    FilterColumnName,
    FilterValue,
    LegalValues,
    DeveloperNotes,
    Active,
    CreatedOn,
    UpdatedOn
FROM {RelationshipsTable}
WHERE DatabaseId = @databaseId
ORDER BY SourceSchemaName, SourceTableName, TargetSchemaName, TargetTableName, RelationshipName;
""";

        var relationships = (await connection.QueryAsync<DatabaseRelationshipDefinition>(sql, new { databaseId })).ToList();
        await LoadColumnsAsync(connection, relationships, RelationshipColumnsTable);
        return relationships;
    }

    public async Task<IReadOnlyList<DatabaseRelationshipDefinition>> GetByTableAsync(
        int databaseId,
        string schemaName,
        string tableName)
    {
        await using var connection = new SqlConnection(_connectionString);

        var sql = $"""
SELECT
    DatabaseRelationshipId,
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    RelationshipRole,
    RelationshipName,
    RelationshipKey,
    SourceSystemDetected,
    UserConfirmed,
    DefaultIncludeInBaseView,
    UseInDomainObjectModeler,
    UseInQueryBuilder,
    DisplayColumnName,
    FilterColumnName,
    FilterValue,
    LegalValues,
    DeveloperNotes,
    Active,
    CreatedOn,
    UpdatedOn
FROM {RelationshipsTable}
WHERE DatabaseId = @databaseId
    AND
    (
        (SourceSchemaName = @schemaName AND SourceTableName = @tableName)
        OR
        (TargetSchemaName = @schemaName AND TargetTableName = @tableName)
    )
ORDER BY SourceSchemaName, SourceTableName, TargetSchemaName, TargetTableName, RelationshipName;
""";

        var relationships = (await connection.QueryAsync<DatabaseRelationshipDefinition>(
            sql,
            new { databaseId, schemaName, tableName })).ToList();

        await LoadColumnsAsync(connection, relationships, RelationshipColumnsTable);
        return relationships;
    }

    public async Task<int> UpsertAsync(DatabaseRelationshipDefinition relationship)
    {
        Normalize(relationship);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var databaseRelationshipId = relationship.DatabaseRelationshipId;

            if (databaseRelationshipId == 0)
            {
                databaseRelationshipId = await FindExistingIdAsync(connection, transaction, relationship, RelationshipsTable);
            }

            if (databaseRelationshipId == 0)
            {
                databaseRelationshipId = await InsertAsync(connection, transaction, relationship, RelationshipsTable);
            }
            else
            {
                relationship.DatabaseRelationshipId = databaseRelationshipId;
                await UpdateAsync(connection, transaction, relationship, RelationshipsTable);
            }

            await ReplaceColumnsAsync(connection, transaction, databaseRelationshipId, relationship.Columns, RelationshipColumnsTable);
            await transaction.CommitAsync();

            relationship.DatabaseRelationshipId = databaseRelationshipId;
            return databaseRelationshipId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAsync(int databaseRelationshipId)
    {
        await using var connection = new SqlConnection(_connectionString);

        var sql = $"""
DELETE FROM {RelationshipsTable}
WHERE DatabaseRelationshipId = @databaseRelationshipId;
""";

        await connection.ExecuteAsync(sql, new { databaseRelationshipId });
    }

    private static async Task LoadColumnsAsync(
        SqlConnection connection,
        List<DatabaseRelationshipDefinition> relationships,
        string relationshipColumnsTable)
    {
        if (relationships.Count == 0)
        {
            return;
        }

        var ids = relationships.Select(relationship => relationship.DatabaseRelationshipId).ToArray();

        var sql = $"""
SELECT
    DatabaseRelationshipColumnId,
    DatabaseRelationshipId,
    OrdinalPosition,
    SourceColumnName,
    TargetColumnName
FROM {relationshipColumnsTable}
WHERE DatabaseRelationshipId IN @ids
ORDER BY DatabaseRelationshipId, OrdinalPosition;
""";

        Dictionary<int, List<DatabaseRelationshipColumnDefinition>> columns;
        try
        {
            columns = (await connection.QueryAsync<DatabaseRelationshipColumnDefinition>(sql, new { ids }))
                .GroupBy(column => column.DatabaseRelationshipId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            foreach (var relationship in relationships)
            {
                relationship.Columns = new List<DatabaseRelationshipColumnDefinition>();
            }

            return;
        }

        foreach (var relationship in relationships)
        {
            relationship.Columns = columns.GetValueOrDefault(relationship.DatabaseRelationshipId) ?? new List<DatabaseRelationshipColumnDefinition>();
        }
    }

    private static async Task<int> FindExistingIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable)
    {
        if (!string.IsNullOrWhiteSpace(relationship.RelationshipKey))
        {
            var keySql = $"""
SELECT TOP (1) DatabaseRelationshipId
FROM {relationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND RelationshipKey = @RelationshipKey;
""";

            return await connection.ExecuteScalarAsync<int?>(
                keySql,
                relationship,
                transaction) ?? 0;
        }

        var naturalSql = $"""
SELECT TOP (1) DatabaseRelationshipId
FROM {relationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND SourceSchemaName = @SourceSchemaName
    AND SourceTableName = @SourceTableName
    AND TargetSchemaName = @TargetSchemaName
    AND TargetTableName = @TargetTableName
    AND RelationshipRole = @RelationshipRole
    AND ISNULL(RelationshipName, N'') = ISNULL(@RelationshipName, N'');
""";

        return await connection.ExecuteScalarAsync<int?>(
            naturalSql,
            relationship,
            transaction) ?? 0;
    }

    private static async Task<int> InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable)
    {
        var sql = $"""
INSERT INTO {relationshipsTable}
(
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    RelationshipRole,
    RelationshipName,
    RelationshipKey,
    SourceSystemDetected,
    UserConfirmed,
    DefaultIncludeInBaseView,
    UseInDomainObjectModeler,
    UseInQueryBuilder,
    DisplayColumnName,
    FilterColumnName,
    FilterValue,
    LegalValues,
    DeveloperNotes,
    Active,
    UpdatedOn
)
OUTPUT INSERTED.DatabaseRelationshipId
VALUES
(
    @DatabaseId,
    @SourceSchemaName,
    @SourceTableName,
    @TargetSchemaName,
    @TargetTableName,
    @JoinType,
    @RelationshipRole,
    @RelationshipName,
    @RelationshipKey,
    @SourceSystemDetected,
    @UserConfirmed,
    @DefaultIncludeInBaseView,
    @UseInDomainObjectModeler,
    @UseInQueryBuilder,
    @DisplayColumnName,
    @FilterColumnName,
    @FilterValue,
    @LegalValues,
    @DeveloperNotes,
    @Active,
    SYSDATETIME()
);
""";

        return await connection.ExecuteScalarAsync<int>(sql, relationship, transaction);
    }

    private static async Task UpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable)
    {
        var sql = $"""
UPDATE {relationshipsTable}
SET
    SourceSchemaName = @SourceSchemaName,
    SourceTableName = @SourceTableName,
    TargetSchemaName = @TargetSchemaName,
    TargetTableName = @TargetTableName,
    JoinType = @JoinType,
    RelationshipRole = @RelationshipRole,
    RelationshipName = @RelationshipName,
    RelationshipKey = @RelationshipKey,
    SourceSystemDetected = @SourceSystemDetected,
    UserConfirmed = @UserConfirmed,
    DefaultIncludeInBaseView = @DefaultIncludeInBaseView,
    UseInDomainObjectModeler = @UseInDomainObjectModeler,
    UseInQueryBuilder = @UseInQueryBuilder,
    DisplayColumnName = @DisplayColumnName,
    FilterColumnName = @FilterColumnName,
    FilterValue = @FilterValue,
    LegalValues = @LegalValues,
    DeveloperNotes = @DeveloperNotes,
    Active = @Active,
    UpdatedOn = SYSDATETIME()
WHERE DatabaseRelationshipId = @DatabaseRelationshipId;
""";

        await connection.ExecuteAsync(sql, relationship, transaction);
    }

    private static async Task ReplaceColumnsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int databaseRelationshipId,
        IReadOnlyList<DatabaseRelationshipColumnDefinition> columns,
        string relationshipColumnsTable)
    {
        var deleteSql = $"""
DELETE FROM {relationshipColumnsTable}
WHERE DatabaseRelationshipId = @databaseRelationshipId;
""";

        try
        {
            await connection.ExecuteAsync(deleteSql, new { databaseRelationshipId }, transaction);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return;
        }

        var insertSql = $"""
INSERT INTO {relationshipColumnsTable}
(
    DatabaseRelationshipId,
    OrdinalPosition,
    SourceColumnName,
    TargetColumnName,
    UpdatedOn
)
VALUES
(
    @DatabaseRelationshipId,
    @OrdinalPosition,
    @SourceColumnName,
    @TargetColumnName,
    SYSDATETIME()
);
""";

        var rows = columns
            .OrderBy(column => column.OrdinalPosition)
            .Select((column, index) => new
            {
                DatabaseRelationshipId = databaseRelationshipId,
                OrdinalPosition = index + 1,
                column.SourceColumnName,
                column.TargetColumnName
            })
            .ToList();

        if (rows.Count > 0)
        {
            try
            {
                await connection.ExecuteAsync(insertSql, rows, transaction);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return;
            }
        }
    }

    private static void Normalize(DatabaseRelationshipDefinition relationship)
    {
        relationship.SourceSchemaName = NormalizeName(relationship.SourceSchemaName, "dbo");
        relationship.SourceTableName = NormalizeName(relationship.SourceTableName, "");
        relationship.TargetSchemaName = NormalizeName(relationship.TargetSchemaName, "dbo");
        relationship.TargetTableName = NormalizeName(relationship.TargetTableName, "");
        relationship.JoinType = NormalizeName(relationship.JoinType, "LEFT JOIN");
        relationship.RelationshipRole = NormalizeName(relationship.RelationshipRole, "Lookup");
        relationship.RelationshipName = NormalizeOptional(relationship.RelationshipName);
        relationship.RelationshipKey = NormalizeOptional(relationship.RelationshipKey);
        relationship.DisplayColumnName = NormalizeOptional(relationship.DisplayColumnName);
        relationship.FilterColumnName = NormalizeOptional(relationship.FilterColumnName);
        relationship.FilterValue = NormalizeOptional(relationship.FilterValue);
        relationship.LegalValues = NormalizeOptional(relationship.LegalValues);
        relationship.DeveloperNotes = NormalizeOptional(relationship.DeveloperNotes);

        foreach (var column in relationship.Columns)
        {
            column.SourceColumnName = NormalizeName(column.SourceColumnName, "");
            column.TargetColumnName = NormalizeName(column.TargetColumnName, "");
        }
    }

    private static string NormalizeName(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string QuoteSqlIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
