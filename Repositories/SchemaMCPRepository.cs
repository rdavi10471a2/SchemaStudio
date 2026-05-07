using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Models;
using System.Text.RegularExpressions;

namespace SchemaStudioWebViewer.Repositories;

[FileVersion("1.1")]
[AIFileContext("Repositories/SchemaMCPRepository.cs", "Read-only Dapper repository for MCP schema discovery tools. Provides the database/domain/object/field lookup chain used by MCP tool wrappers and the Tool Lab debug page.", Responsibilities = "Owns read-only Schema Studio metadata queries for AI-facing schema discovery without exposing write operations.", Nuances = "Keep this repository query-focused and async; tool wrappers own exception-to-tool-response conversion so failures stay structured for AI callers.", LastReviewed = "2026-05-07")]
public sealed class SchemaMCPRepository
{
    private readonly string connectionString;

    public SchemaMCPRepository(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DatabaseModel>> GetDatabasesAsync()
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    Active
FROM Databases
WHERE Active = 1
ORDER BY DatabaseName";

        var rows = await connection.QueryAsync<DatabaseModel>(sql);
        return rows.ToList();
    }

    public async Task<DatabaseModel?> GetDatabaseAsync(int databaseId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    Active
FROM Databases
WHERE DatabaseId = @databaseId
AND Active = 1";

        return await connection.QueryFirstOrDefaultAsync<DatabaseModel>(sql, new { databaseId });
    }

    public async Task<IReadOnlyList<DatabaseDomainModel>> GetDomainsAsync(int databaseId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain
FROM DatabaseDomain
WHERE DatabaseId = @databaseId
ORDER BY Domain";

        var rows = await connection.QueryAsync<DatabaseDomainModel>(sql, new { databaseId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SchemaObjectModel>> GetSchemaObjectsAsync(int databaseId, string? domain)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    IsActive,
    LastSynced
FROM SchemaObject
WHERE DatabaseId = @databaseId
AND IsActive = 1
AND (@domain IS NULL OR Domain = @domain)
ORDER BY Domain, IsBaseObject DESC, COALESCE(BusinessName, SourceObjectName), SourceSchemaName, SourceObjectName";

        var normalizedDomain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim();
        var rows = await connection.QueryAsync<SchemaObjectModel>(
            sql,
            new { databaseId, domain = normalizedDomain });

        return rows.ToList();
    }

    public async Task<SchemaObjectModel?> GetSchemaObjectAsync(int schemaObjectId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    SchemaObjectId,
    DatabaseId,
    SourceDatabaseName,
    SourceSchemaName,
    SourceObjectName,
    IsBaseObject,
    Domain,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    IsActive,
    LastSynced
FROM SchemaObject
WHERE SchemaObjectId = @schemaObjectId
AND IsActive = 1";

        return await connection.QueryFirstOrDefaultAsync<SchemaObjectModel>(sql, new { schemaObjectId });
    }

    public async Task<IReadOnlyList<SchemaObjectColumnModel>> GetFieldsAsync(int schemaObjectId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    SchemaObjectColumnId,
    SchemaObjectId,
    OrdinalPosition,
    SourceColumnName,
    SourceColumnKind,
    BaseDatabaseName,
    BaseSchemaName,
    BaseObjectName,
    BaseColumnName,
    IsBaseDefinition,
    DisableInheritance,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    LastSynced
FROM SchemaObjectColumn
WHERE SchemaObjectId = @schemaObjectId
ORDER BY OrdinalPosition, SourceColumnName";

        var rows = await connection.QueryAsync<SchemaObjectColumnModel>(sql, new { schemaObjectId });
        return rows.ToList();
    }

    public async Task<SchemaObjectColumnModel?> GetFieldAsync(int schemaObjectColumnId)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    SchemaObjectColumnId,
    SchemaObjectId,
    OrdinalPosition,
    SourceColumnName,
    SourceColumnKind,
    BaseDatabaseName,
    BaseSchemaName,
    BaseObjectName,
    BaseColumnName,
    IsBaseDefinition,
    DisableInheritance,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    LastSynced
FROM SchemaObjectColumn
WHERE SchemaObjectColumnId = @schemaObjectColumnId";

        return await connection.QueryFirstOrDefaultAsync<SchemaObjectColumnModel>(sql, new { schemaObjectColumnId });
    }

    public async Task<ViewDefinitionResult?> GetViewSqlAsync(int schemaObjectId)
    {
        var schemaObject = await GetSchemaObjectAsync(schemaObjectId);
        if (schemaObject is null)
        {
            return null;
        }

        return await GetViewSqlAsync(schemaObject);
    }

    public async Task<ViewDefinitionResult?> GetViewSqlAsync(SchemaObjectModel schemaObject)
    {
        if (string.IsNullOrWhiteSpace(schemaObject.SourceDatabaseName))
        {
            throw new ArgumentException("Schema object does not have a source database name.");
        }

        if (string.IsNullOrWhiteSpace(schemaObject.SourceSchemaName))
        {
            throw new ArgumentException("Schema object does not have a source schema name.");
        }

        if (string.IsNullOrWhiteSpace(schemaObject.SourceObjectName))
        {
            throw new ArgumentException("Schema object does not have a source object name.");
        }

        var databaseName = BracketSqlIdentifier(schemaObject.SourceDatabaseName);

        var sql = $@"
SELECT
    sm.definition AS Definition,
    v.modify_date AS ModifyDate
FROM {databaseName}.sys.views v
INNER JOIN {databaseName}.sys.schemas s
    ON v.schema_id = s.schema_id
INNER JOIN {databaseName}.sys.sql_modules sm
    ON v.object_id = sm.object_id
WHERE s.name = @schemaName
AND v.name = @objectName";

        await using var connection = new SqlConnection(connectionString);
        return await connection.QueryFirstOrDefaultAsync<ViewDefinitionResult>(
            sql,
            new
            {
                schemaName = schemaObject.SourceSchemaName,
                objectName = schemaObject.SourceObjectName
            });
    }

    public static string CleanSqlDefinition(string rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
        {
            return string.Empty;
        }

        const string pattern = @"/\*\s*@BusinessName.*?\*/";

        return Regex.Replace(
            rawSql,
            pattern,
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
    }

    private static string BracketSqlIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("SQL identifier is required.", nameof(identifier));
        }

        return $"[{identifier.Replace("]", "]]")}]";
    }
}
