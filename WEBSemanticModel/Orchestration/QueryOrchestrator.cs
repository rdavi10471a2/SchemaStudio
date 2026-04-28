using SchemaStudioWebViewer.WEBSemanticModel.Binding;
using SchemaStudioWebViewer.WEBSemanticModel.Diagnostics;
using SchemaStudioWebViewer.WEBSemanticModel.Model;
using SchemaStudioWebViewer.WEBSemanticModel.Parsing;
using SchemaStudioWebViewer.WEBSemanticModel.Providers;
using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.WEBSemanticModel.Orchestration
{
    [FileVersion("1.0")]
    [AIInstructions("2026-03-30 15:53 preserve inherited database/schema context when resolving nested view dependencies and avoid forced dbo fallback.", AICommandStatus.Pending)]
    public static class QueryOrchestrator
    {
        //-----------------------------------------
        // ENTRY POINT (LOGGER REQUIRED)
        //-----------------------------------------
        public static ParsedQuery ParseFully(
            string sql,
            string database,
            string schema,
            string viewName,
            IViewDefinitionProvider provider,
            IQueryLogger logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            logger.Info($"Orchestrator START: {database}.{schema}.{viewName}");

            #region DontEditMisionCritical

            try
            {
                //-----------------------------------------
                // STEP 1: PARSE ROOT
                //-----------------------------------------
                var parser = new ViewParser();
                parser.Logger = logger;

                var root = parser.Parse(sql);

                if (root == null)
                {
                    logger.Warning("Root parse returned null");
                    return null;
                }

                //-----------------------------------------
                // STEP 2: RESOLVE DEPENDENCIES (WITH LOOP GUARD)
                //-----------------------------------------
                ResolveDependencies(
                    root,
                    database,
                    schema,
                    provider,
                    logger,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                );

                //-----------------------------------------
                // 3. BIND DIRECT COLUMN REFERENCES
                //-----------------------------------------
                ColumnBinder.Bind(root);

                //-----------------------------------------
                // 4. PROJECT THROUGH DERIVED / VIEWS
                //-----------------------------------------
                QueryBinder.Bind(root);

                //-----------------------------------------
                // 5. ASSIGN VIEW OWNERSHIP
                //-----------------------------------------
               
                ViewOwnershipBinder.Apply(root, database, schema, viewName);
                ViewMetadataBinder.Apply(root);

                logger.Info("Orchestrator COMPLETE");

                return root;
            }
            catch (Exception ex)
            {
                logger.Error($"FAILED Orchestrator.ParseFully: {database}.{schema}.{viewName}", ex);
                throw;
            }

            #endregion
        }

        //-----------------------------------------
        // VIEW OWNERSHIP BINDER (UNCHANGED)
        //----------------------------------------


        public static class ViewOwnershipBinder
        {
            public static void Apply(ParsedQuery query, string db, string schema, string objectName)
            {
                if (query == null)
                    return;

                foreach (var item in query.SelectItems)
                {
                    bool isPhysical =
                        item.Kind == ColumnKind.Simple &&
                        !string.IsNullOrWhiteSpace(item.BaseTable) &&
                        !string.IsNullOrWhiteSpace(item.BaseColumn);

                    if (!isPhysical)
                    {
                        // Prefer preserved upstream expression ownership when QueryBinder found
                        // that this projected expression came from an inner view expression.
                        if (!string.IsNullOrWhiteSpace(item.ExpressionTable) &&
                            !string.IsNullOrWhiteSpace(item.ExpressionColumn))
                        {
                            item.BaseDatabase = item.ExpressionDatabase;
                            item.BaseSchema = item.ExpressionSchema;
                            item.BaseTable = item.ExpressionTable;
                            item.BaseColumn = item.ExpressionColumn;
                        }
                        else
                        {
                            item.BaseDatabase = db;
                            item.BaseSchema = schema;
                            item.BaseTable = objectName;
                            item.BaseColumn = item.Alias;
                        }

                      
                    }
                    else
                    {
                        
                    }
                }
            }
        }

        //-----------------------------------------
        // DEPENDENCY RESOLUTION (FIXED)
        //-----------------------------------------
        private static void ResolveDependencies(
            ParsedQuery query,
            string currentDb,
            string currentSchema,
            IViewDefinitionProvider provider,
            IQueryLogger logger,
            HashSet<string> visited)
        {
            if (query == null)
                return;

            foreach (var source in query.SourceTables)
            {
                //-----------------------------------------
                // HANDLE DERIVED FIRST
                //-----------------------------------------
                if (source.NestedQuery != null)
                {
                    logger.Info($"Resolving nested query: {source.Alias}");
                    ResolveDependencies(source.NestedQuery, currentDb, currentSchema, provider, logger, visited);
                    continue;
                }

                //-----------------------------------------
                // SKIP NON-TABLE
                //-----------------------------------------
                if (string.IsNullOrWhiteSpace(source.Table))
                    continue;

                var db = source.Database ?? currentDb;
                var schema = source.Schema ?? currentSchema ?? "dbo";
                var table = source.Table;

                source.Database ??= db;
                source.Schema ??= schema;

                // 15:53 context marker: preserve inherited schema/database for unqualified nested view references.
                var key = $"{db}.{schema}.{table}".ToLowerInvariant();

                //-----------------------------------------
                // LOOP PROTECTION
                //-----------------------------------------
                if (!visited.Add(key))
                {
                    logger.Warning($"Skipping already visited: {key}");
                    continue;
                }

                logger.Info($"Resolving: {db}.{schema}.{table}");

                try
                {
                    //-----------------------------------------
                    // GET SQL (CACHE HIT/MISS HERE)
                    //-----------------------------------------
                    var sql = provider.GetViewDefinition(db, schema, table);

                    //-----------------------------------------
                    // NOT A VIEW OR NOT FOUND
                    //-----------------------------------------
                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        logger.Info($"No SQL (likely base table): {db}.{schema}.{table}");
                        continue;
                    }

                    //-----------------------------------------
                    // PARSE CHILD
                    //-----------------------------------------
                    var parser = new ViewParser();
                    parser.Logger = logger;

                    var child = parser.Parse(sql);

                    source.NestedQuery = child;

                    //-----------------------------------------
                    // RECURSE
                    //-----------------------------------------
                    ResolveDependencies(child, db, schema, provider, logger, visited);
                }
                catch (Exception ex)
                {
                    logger.Warning($"Dependency failed: {db}.{schema}.{table} | {ex.Message}");
                }
            }
        }
    }
}

