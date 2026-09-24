using Microsoft.Data.SqlClient;

namespace SchemaStudioWebViewer.Data
{
    /// <summary>
    /// Executes generated DDL against the app's default SQL connection, overriding only the default
    /// database (InitialCatalog) so each artifact lands in its own target database. The generated "core"
    /// scripts are single, GO-free batches (the USE/GO wrapper shown in the UI is display-only), so each
    /// runs as one command -- no GO batch-splitting is required or attempted.
    /// </summary>
    public class SqlScriptExecutionRepository
    {
        private readonly string _connectionString;

        public SqlScriptExecutionRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task ExecuteAsync(string? targetDatabase, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new ArgumentException("No SQL to execute.", nameof(sql));
            }

            // Reuse the shared connection (same server/credentials as the read path) but point the
            // login's default database at the requested target. Nothing global is mutated.
            var builder = new SqlConnectionStringBuilder(_connectionString);
            if (!string.IsNullOrWhiteSpace(targetDatabase))
            {
                builder.InitialCatalog = targetDatabase.Trim();
            }

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
        }
    }
}
