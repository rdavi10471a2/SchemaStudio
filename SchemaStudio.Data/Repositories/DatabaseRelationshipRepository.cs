using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

    [FileVersion("1.8")]
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
        var joinExpressionProjection = await HasColumnAsync(connection, "DatabaseRelationships", "JoinExpression")
            ? "JoinExpression,"
            : "CAST(N'' AS nvarchar(2000)) AS JoinExpression,";
        var sourceToTargetRoleProjection = await HasColumnAsync(connection, "DatabaseRelationships", "SourceToTargetRole")
            ? "SourceToTargetRole,"
            : "RelationshipRole AS SourceToTargetRole,";
        var targetToSourceRoleProjection = await HasColumnAsync(connection, "DatabaseRelationships", "TargetToSourceRole")
            ? "TargetToSourceRole,"
            : "CAST(N'ReferencedBy' AS nvarchar(32)) AS TargetToSourceRole,";

        var sql = $"""
SELECT
    DatabaseRelationshipId,
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    {joinExpressionProjection}
    RelationshipRole,
    {sourceToTargetRoleProjection}
    {targetToSourceRoleProjection}
    RelationshipName,
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
        var joinExpressionProjection = await HasColumnAsync(connection, "DatabaseRelationships", "JoinExpression")
            ? "JoinExpression,"
            : "CAST(N'' AS nvarchar(2000)) AS JoinExpression,";
        var sourceToTargetRoleProjection = await HasColumnAsync(connection, "DatabaseRelationships", "SourceToTargetRole")
            ? "SourceToTargetRole,"
            : "RelationshipRole AS SourceToTargetRole,";
        var targetToSourceRoleProjection = await HasColumnAsync(connection, "DatabaseRelationships", "TargetToSourceRole")
            ? "TargetToSourceRole,"
            : "CAST(N'ReferencedBy' AS nvarchar(32)) AS TargetToSourceRole,";

        var sql = $"""
SELECT
    DatabaseRelationshipId,
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    {joinExpressionProjection}
    RelationshipRole,
    {sourceToTargetRoleProjection}
    {targetToSourceRoleProjection}
    RelationshipName,
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
    AND SourceSchemaName = @schemaName
    AND SourceTableName = @tableName
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
            var hasDirectionalRoles = await HasColumnAsync(connection, "DatabaseRelationships", "SourceToTargetRole", transaction) &&
                await HasColumnAsync(connection, "DatabaseRelationships", "TargetToSourceRole", transaction);
            var databaseRelationshipId = relationship.DatabaseRelationshipId;

            if (databaseRelationshipId == 0)
            {
                databaseRelationshipId = await FindExistingIdAsync(
                    connection,
                    transaction,
                    relationship,
                    RelationshipsTable,
                    RelationshipColumnsTable);
            }

            if (databaseRelationshipId == 0)
            {
                databaseRelationshipId = await InsertAsync(connection, transaction, relationship, RelationshipsTable, hasDirectionalRoles);
            }
            else
            {
                relationship.DatabaseRelationshipId = databaseRelationshipId;
                await UpdateAsync(connection, transaction, relationship, RelationshipsTable, hasDirectionalRoles);
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

    private async Task<bool> HasColumnAsync(
        SqlConnection connection,
        string tableName,
        string columnName,
        SqlTransaction? transaction = null)
    {
        var sql = $"""
SELECT COUNT(1)
FROM {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.columns AS c
JOIN {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.objects AS o
    ON o.object_id = c.object_id
JOIN {QuoteSqlIdentifier(_metadataDatabaseName)}.sys.schemas AS s
    ON s.schema_id = o.schema_id
WHERE s.name = N'dbo'
    AND o.name = @tableName
    AND c.name = @columnName;
""";

        return await connection.ExecuteScalarAsync<int>(sql, new { tableName, columnName }, transaction) > 0;
    }

    private static async Task<int> FindExistingIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable,
        string relationshipColumnsTable)
    {
        if (relationship.Columns.Count > 0)
        {
            var candidatesSql = $"""
SELECT DatabaseRelationshipId
FROM {relationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND SourceSchemaName = @SourceSchemaName
    AND SourceTableName = @SourceTableName
    AND TargetSchemaName = @TargetSchemaName
    AND TargetTableName = @TargetTableName;
""";

            var candidateIds = (await connection.QueryAsync<int>(
                candidatesSql,
                relationship,
                transaction)).ToList();

            if (candidateIds.Count == 0)
            {
                return 0;
            }

            var columnsSql = $"""
SELECT
    DatabaseRelationshipId,
    OrdinalPosition,
    SourceColumnName,
    TargetColumnName
FROM {relationshipColumnsTable}
WHERE DatabaseRelationshipId IN @candidateIds
ORDER BY DatabaseRelationshipId, OrdinalPosition;
""";

            var candidateColumns = (await connection.QueryAsync<DatabaseRelationshipColumnDefinition>(
                columnsSql,
                new { candidateIds },
                transaction))
                .GroupBy(column => column.DatabaseRelationshipId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var expectedColumns = NormalizeColumnPairs(relationship.Columns);

            foreach (var candidateId in candidateIds)
            {
                var actualColumns = NormalizeColumnPairs(candidateColumns.GetValueOrDefault(candidateId) ?? new List<DatabaseRelationshipColumnDefinition>());
                if (ColumnsMatch(expectedColumns, actualColumns))
                {
                    return candidateId;
                }
            }

            if (!string.IsNullOrWhiteSpace(relationship.RelationshipName))
            {
                var namedCandidateSql = $"""
SELECT TOP (1) DatabaseRelationshipId
FROM {relationshipsTable}
WHERE DatabaseId = @DatabaseId
    AND SourceSchemaName = @SourceSchemaName
    AND SourceTableName = @SourceTableName
    AND TargetSchemaName = @TargetSchemaName
    AND TargetTableName = @TargetTableName
    AND ISNULL(RelationshipName, N'') = ISNULL(@RelationshipName, N'');
""";

                return await connection.ExecuteScalarAsync<int?>(
                    namedCandidateSql,
                    relationship,
                    transaction) ?? 0;
            }

            return 0;
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
    AND JoinType = @JoinType
    AND ISNULL(RelationshipName, N'') = ISNULL(@RelationshipName, N'');
""";

        return await connection.ExecuteScalarAsync<int?>(
            naturalSql,
            relationship,
            transaction) ?? 0;
    }

    private static bool ColumnsMatch(
        IReadOnlyList<DatabaseRelationshipColumnDefinition> expectedColumns,
        IReadOnlyList<DatabaseRelationshipColumnDefinition> actualColumns)
    {
        if (expectedColumns.Count != actualColumns.Count)
        {
            return false;
        }

        for (var index = 0; index < expectedColumns.Count; index++)
        {
            if (!string.Equals(expectedColumns[index].SourceColumnName, actualColumns[index].SourceColumnName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(expectedColumns[index].TargetColumnName, actualColumns[index].TargetColumnName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static List<DatabaseRelationshipColumnDefinition> NormalizeColumnPairs(IEnumerable<DatabaseRelationshipColumnDefinition> columns) =>
        columns
            .OrderBy(column => column.SourceColumnName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(column => column.TargetColumnName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static async Task<int> InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DatabaseRelationshipDefinition relationship,
        string relationshipsTable,
        bool hasDirectionalRoles)
    {
        var directionalColumns = hasDirectionalRoles ? """
    SourceToTargetRole,
    TargetToSourceRole,
""" : "";
        var directionalValues = hasDirectionalRoles ? """
    @SourceToTargetRole,
    @TargetToSourceRole,
""" : "";
        var sql = $"""
INSERT INTO {relationshipsTable}
(
    DatabaseId,
    SourceSchemaName,
    SourceTableName,
    TargetSchemaName,
    TargetTableName,
    JoinType,
    JoinExpression,
    RelationshipRole,
{directionalColumns}
    RelationshipName,
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
    @JoinExpression,
    @RelationshipRole,
{directionalValues}
    @RelationshipName,
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
        string relationshipsTable,
        bool hasDirectionalRoles)
    {
        var directionalSet = hasDirectionalRoles ? """
    SourceToTargetRole = @SourceToTargetRole,
    TargetToSourceRole = @TargetToSourceRole,
""" : "";
        var sql = $"""
UPDATE {relationshipsTable}
SET
    SourceSchemaName = @SourceSchemaName,
    SourceTableName = @SourceTableName,
    TargetSchemaName = @TargetSchemaName,
    TargetTableName = @TargetTableName,
    JoinType = @JoinType,
    JoinExpression = @JoinExpression,
    RelationshipRole = @RelationshipRole,
{directionalSet}
    RelationshipName = @RelationshipName,
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
        relationship.JoinExpression = NormalizeName(relationship.JoinExpression, "");
        relationship.RelationshipRole = NormalizeName(relationship.RelationshipRole, "Lookup");
        relationship.SourceToTargetRole = NormalizeName(relationship.SourceToTargetRole, relationship.RelationshipRole);
        relationship.TargetToSourceRole = NormalizeName(relationship.TargetToSourceRole, "ReferencedBy");
        relationship.RelationshipName = NormalizeOptional(relationship.RelationshipName);
        relationship.DisplayColumnName = NormalizeOptional(relationship.DisplayColumnName);
        relationship.FilterColumnName = NormalizeOptional(relationship.FilterColumnName);
        relationship.FilterValue = NormalizeOptional(relationship.FilterValue);
        relationship.LegalValues = NormalizeOptional(relationship.LegalValues);
        relationship.DeveloperNotes = NormalizeOptional(relationship.DeveloperNotes);

        foreach (var column in relationship.Columns)
        {
            column.SourceColumnName = NormalizeColumnName(column.SourceColumnName);
            column.TargetColumnName = NormalizeColumnName(column.TargetColumnName);
        }
    }

    private static string NormalizeName(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeColumnName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            var openBracket = trimmed.LastIndexOf('[', trimmed.Length - 1);
            if (openBracket >= 0 && openBracket < trimmed.Length - 1)
            {
                return trimmed[(openBracket + 1)..^1];
            }
        }

        var dot = trimmed.LastIndexOf('.');
        return dot >= 0 && dot < trimmed.Length - 1
            ? trimmed[(dot + 1)..].Trim('[', ']')
            : trimmed.Trim('[', ']');
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string QuoteSqlIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
