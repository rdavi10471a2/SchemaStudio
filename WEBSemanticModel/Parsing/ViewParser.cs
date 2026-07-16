using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaStudioWebViewer.WEBSemanticModel.Model;
using SchemaStudioWebViewer.WEBSemanticModel.Diagnostics;

namespace SchemaStudioWebViewer.WEBSemanticModel.Parsing
{
    public class ViewParser
    {
        //-----------------------------------------
        // OPTIONAL LOGGER (NON-BREAKING)
        //-----------------------------------------
        public IQueryLogger Logger { get; set; }

        private IQueryLogger Log => Logger ?? new NullQueryLogger();

        public ParsedQuery Parse(string sql)
        {
            Log.Info("ViewParser.Parse START");

            //-----------------------------------------
            // PRE-FLIGHT: ENCRYPTED / EMPTY
            //-----------------------------------------
            if (string.IsNullOrWhiteSpace(sql))
            {
                Log.Error("SQL is empty or unavailable");
                throw new Exception("View definition unavailable (likely encrypted).");
            }

            //-----------------------------------------
            // OPTIONAL FORMAT PASS
            //-----------------------------------------
            string workingSql = sql;

            if (SqlFormatter.NeedsFormatting(sql))
            {
                Log.Info("Formatting SQL");

                var tempParser = new TSql160Parser(true);

                using (var tempReader = new StringReader(sql))
                {
                    var tempFragment = tempParser.Parse(tempReader, out _);
                    workingSql = SqlFormatter.Format(tempFragment.ScriptTokenStream);
                }
            }

            //-----------------------------------------
            // MAIN PARSE
            //-----------------------------------------
            Log.Info("Parsing SQL AST");

            var parser = new TSql160Parser(true);

            using (var reader = new StringReader(workingSql))
            {
                var fragment = parser.Parse(reader, out var errors);

                if (errors.Count > 0)
                {
                    var parseErrorMessage = BuildParseErrorMessage(errors, workingSql);
                    Log.Error(parseErrorMessage);
                    throw new Exception(parseErrorMessage);
                }

                //-----------------------------------------
                // FIND OUTERMOST SELECT
                //-----------------------------------------
                Log.Info("Finding outermost SELECT");

                SelectStatement selectStatement = null;

                fragment.Accept(new OutermostSelectStatementFinder(s => selectStatement = s));

                if (selectStatement == null)
                {
                    Log.Error("No valid SELECT found");
                    throw new Exception("No valid SELECT statement found.");
                }

                var spec = BasicSelectVisitor.TryGetQuerySpecification(selectStatement.QueryExpression);

                if (spec == null)
                {
                    Log.Error("No valid SELECT query specification found");
                    throw new Exception("No valid SELECT query specification found.");
                }

                var cteMap = ParseCommonTableExpressions(selectStatement, fragment.ScriptTokenStream);

                //-----------------------------------------
                // VISITOR
                //-----------------------------------------
                Log.Info("Running BasicSelectVisitor");

                var visitor = new BasicSelectVisitor(fragment.ScriptTokenStream, cteMap);
                visitor.Parse(spec);

                var result = visitor.Result;
                result.SourceQuery = workingSql;

                Log.Info($"Parse complete: {result.SelectItems.Count} select items, {result.SourceTables.Count} tables");

                return result;
            }
        }

        //-----------------------------------------
        // PARSE ERROR DETAIL
        //-----------------------------------------
        // Surfaces the full ScriptDom parse-error detail (error number, line, column) for
        // every reported error, plus the offending SQL line with a caret under the column,
        // instead of collapsing to the first error's bare message.
        private static string BuildParseErrorMessage(IList<ParseError> errors, string sql)
        {
            var sqlLines = (sql ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');

            var builder = new System.Text.StringBuilder();
            builder.Append("SQL Parse Error");

            if (errors.Count > 1)
            {
                builder.Append(" (").Append(errors.Count).Append(" errors)");
            }

            builder.Append(':');

            foreach (var error in errors)
            {
                builder.Append(System.Environment.NewLine);
                builder.Append("  [Msg ").Append(error.Number)
                    .Append(", Line ").Append(error.Line)
                    .Append(", Col ").Append(error.Column)
                    .Append("] ").Append(error.Message);

                if (error.Line >= 1 && error.Line <= sqlLines.Length)
                {
                    var offendingLine = sqlLines[error.Line - 1].TrimEnd();

                    builder.Append(System.Environment.NewLine);
                    builder.Append("    ").Append(offendingLine);

                    if (error.Column >= 1 && error.Column <= offendingLine.Length + 1)
                    {
                        builder.Append(System.Environment.NewLine);
                        builder.Append("    ").Append(new string(' ', error.Column - 1)).Append('^');
                    }
                }
            }

            return builder.ToString();
        }

        private Dictionary<string, ParsedQuery> ParseCommonTableExpressions(
            SelectStatement selectStatement,
            IList<TSqlParserToken> tokens)
        {
            var cteMap = new Dictionary<string, ParsedQuery>(StringComparer.OrdinalIgnoreCase);
            var ctes = selectStatement.WithCtesAndXmlNamespaces?.CommonTableExpressions;

            if (ctes == null || ctes.Count == 0)
            {
                return cteMap;
            }

            foreach (var cte in ctes)
            {
                var name = cte.ExpressionName?.Value;
                var spec = BasicSelectVisitor.TryGetQuerySpecification(cte.QueryExpression);

                if (string.IsNullOrWhiteSpace(name) || spec == null)
                {
                    continue;
                }

                Log.Info($"Parsing CTE {name}");

                var visitor = new BasicSelectVisitor(tokens, cteMap);
                visitor.Parse(spec);
                cteMap[name] = visitor.Result;
            }

            return cteMap;
        }

        private class OutermostSelectStatementFinder : TSqlFragmentVisitor
        {
            private readonly Action<SelectStatement> _found;
            private bool _captured;

            public OutermostSelectStatementFinder(Action<SelectStatement> found)
            {
                _found = found;
            }

            public override void Visit(SelectStatement node)
            {
                if (_captured)
                    return;

                _found(node);
                _captured = true;
            }
        }
    }
}
