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
                    Log.Error("SQL Parse Error: " + errors[0].Message);
                    throw new Exception("SQL Parse Error: " + errors[0].Message);
                }

                //-----------------------------------------
                // FIND OUTERMOST QUERY ONLY
                //-----------------------------------------
                Log.Info("Finding outermost SELECT");

                QuerySpecification spec = null;

                fragment.Accept(new OutermostQuerySpecificationFinder(s => spec = s));

                if (spec == null)
                {
                    Log.Error("No valid SELECT found");
                    throw new Exception("No valid SELECT statement found.");
                }

                //-----------------------------------------
                // VISITOR
                //-----------------------------------------
                Log.Info("Running BasicSelectVisitor");

                var visitor = new BasicSelectVisitor(fragment.ScriptTokenStream);
                visitor.Parse(spec);

                var result = visitor.Result;
                result.SourceQuery = workingSql;

                Log.Info($"Parse complete: {result.SelectItems.Count} select items, {result.SourceTables.Count} tables");

                return result;
            }
        }

        private class OutermostQuerySpecificationFinder : TSqlFragmentVisitor
        {
            private readonly Action<QuerySpecification> _found;
            private bool _captured;

            public OutermostQuerySpecificationFinder(Action<QuerySpecification> found)
            {
                _found = found;
            }

            public override void Visit(QuerySpecification node)
            {
                if (_captured)
                    return;

                _found(node);
                _captured = true;
            }
        }
    }
}
