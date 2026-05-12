using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaStudio.AIHelpers;
using System.Text;

namespace SchemaStudioWebViewer.Components.Pages.ObjectShaper;

[FileVersion("1.0")]
[AIFileContext(
    "Components/Pages/ObjectShaper/ObjectShapeSqlParser.cs",
    "Parses regular shaped SQL SELECT or CREATE VIEW text into an object-shaping model for the Object Shaper page.",
    Responsibilities = "Use ScriptDom to capture SELECT projections, source tables, joins, WHERE dependencies, and optional CREATE VIEW target names without querying database metadata.",
    Nuances = "This parser intentionally treats a CTE as out of scope for this page; Object Shaper consumes already-composed regular SELECT shapes produced by upstream tools.")]
public sealed class ObjectShapeSqlParser
{
    public ObjectShapeParseResult Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return ObjectShapeParseResult.Failed("Paste a regular SELECT or CREATE VIEW statement.");
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
        {
            return ObjectShapeParseResult.Failed(string.Join(
                Environment.NewLine,
                errors.Select(error => $"Parse error line {error.Line}, column {error.Column}: {error.Message}")));
        }

        var builder = new ObjectShapeModelBuilder(fragment.ScriptTokenStream);
        fragment.Accept(builder);

        if (builder.Result.Select.Fields.Count == 0)
        {
            return ObjectShapeParseResult.Failed("No SELECT projection was found. This page expects one regular shaped SELECT.");
        }

        return builder.Result;
    }

    public string GenerateSql(ObjectShapeParseResult result, IReadOnlySet<string> selectedFieldKeys)
    {
        if (!result.IsSuccess)
        {
            return result.ErrorMessage;
        }

        var activeAliases = ResolveRequiredAliases(result.Select, selectedFieldKeys);
        var selectedFields = result.Select.Fields
            .Where(field => selectedFieldKeys.Contains(field.Key))
            .ToList();

        if (selectedFields.Count == 0)
        {
            return "-- Select at least one output field.";
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.TargetViewName))
        {
            sb.AppendLine($"CREATE OR ALTER VIEW {result.TargetViewName}");
            sb.AppendLine("AS");
        }

        sb.AppendLine("SELECT");
        for (var index = 0; index < selectedFields.Count; index++)
        {
            var comma = index == 0 ? "      " : "    , ";
            sb.AppendLine($"{comma}{selectedFields[index].ToSelectSql()}");
        }

        var root = result.Select.Sources.FirstOrDefault();
        if (root is not null)
        {
            sb.AppendLine($"FROM {root.DisplayText}");
        }

        foreach (var join in result.Select.Joins.Where(join => activeAliases.Contains(join.RightAlias)))
        {
            sb.AppendLine(join.DisplayText);
        }

        if (!string.IsNullOrWhiteSpace(result.Select.WhereClauseText))
        {
            sb.AppendLine($"WHERE {result.Select.WhereClauseText}");
        }

        return sb.ToString().TrimEnd();
    }

    public IReadOnlySet<string> ResolveRequiredAliases(ObjectShapeSelect select, IReadOnlySet<string> selectedFieldKeys)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (select.Sources.FirstOrDefault() is { } root)
        {
            required.Add(root.Alias);
        }

        foreach (var field in select.Fields.Where(field => selectedFieldKeys.Contains(field.Key)))
        {
            foreach (var reference in field.ColumnRefs)
            {
                required.Add(reference.Alias);
            }
        }

        foreach (var reference in select.WhereColumnRefs)
        {
            required.Add(reference.Alias);
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var join in select.Joins)
            {
                if (!required.Contains(join.RightAlias))
                {
                    continue;
                }

                foreach (var alias in join.ColumnRefs.Select(reference => reference.Alias))
                {
                    if (required.Add(alias))
                    {
                        changed = true;
                    }
                }
            }
        }

        return required;
    }

    private sealed class ObjectShapeModelBuilder : TSqlFragmentVisitor
    {
        private readonly IList<TSqlParserToken> tokens;
        private bool capturedSelect;

        public ObjectShapeModelBuilder(IList<TSqlParserToken> tokens)
        {
            this.tokens = tokens;
        }

        public ObjectShapeParseResult Result { get; } = ObjectShapeParseResult.Success();

        public override void ExplicitVisit(CreateViewStatement node)
        {
            Result.TargetViewName = FormatSchemaObjectName(node.SchemaObjectName);
        }

        public override void ExplicitVisit(SelectStatement node)
        {
            if (capturedSelect)
            {
                return;
            }

            if (node.WithCtesAndXmlNamespaces?.CommonTableExpressions.Count > 0)
            {
                Result.ErrorMessage = "CTE input is intentionally out of scope for Object Shaper. Paste the regular SELECT shape produced by the previous tool.";
                return;
            }

            Result.Select = ParseQueryBody(node.QueryExpression);
            capturedSelect = true;
        }

        private ObjectShapeSelect ParseQueryBody(QueryExpression queryExpression)
        {
            var select = new ObjectShapeSelect();
            var spec = TryGetQuerySpecification(queryExpression);
            if (spec is null)
            {
                return select;
            }

            foreach (var field in ExtractFields(spec))
            {
                select.Fields.Add(field);
            }

            if (spec.FromClause is not null)
            {
                foreach (var tableReference in spec.FromClause.TableReferences)
                {
                    ParseTableReference(tableReference, select);
                }
            }

            if (spec.WhereClause?.SearchCondition is not null)
            {
                select.WhereClauseText = GetFragmentText(spec.WhereClause.SearchCondition);
                select.WhereColumnRefs = ExtractColumnRefs(spec.WhereClause.SearchCondition);
            }

            return select;
        }

        private List<ObjectShapeField> ExtractFields(QuerySpecification spec)
        {
            var fields = new List<ObjectShapeField>();
            foreach (var element in spec.SelectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        fields.Add(ParseField(scalar));
                        break;
                    case SelectStarExpression:
                        fields.Add(new ObjectShapeField(
                            $"field:{fields.Count}:*",
                            "*",
                            "*",
                            null,
                            null,
                            false,
                            Array.Empty<ObjectShapeColumnRef>()));
                        break;
                }
            }

            return fields;
        }

        private ObjectShapeField ParseField(SelectScalarExpression scalar)
        {
            var expressionText = GetFragmentText(scalar.Expression);
            var outputName = scalar.ColumnName?.Value;
            string? sourceAlias = null;
            string? sourceColumn = null;

            if (scalar.Expression is ColumnReferenceExpression columnReference)
            {
                var identifiers = columnReference.MultiPartIdentifier?.Identifiers;
                if (identifiers is not null && identifiers.Count > 0)
                {
                    sourceColumn = identifiers[^1].Value;
                    if (identifiers.Count >= 2)
                    {
                        sourceAlias = identifiers[^2].Value;
                    }

                    outputName ??= sourceColumn;
                }
            }

            outputName ??= expressionText;
            return new ObjectShapeField(
                $"field:{scalar.FirstTokenIndex}:{outputName}",
                outputName,
                expressionText,
                sourceAlias,
                sourceColumn,
                scalar.ColumnName is not null,
                ExtractColumnRefs(scalar.Expression));
        }

        private ObjectShapeSource ParseTableReference(TableReference tableReference, ObjectShapeSelect select)
        {
            switch (tableReference)
            {
                case QualifiedJoin join:
                    ParseTableReference(join.FirstTableReference, select);
                    var right = ParseTableReference(join.SecondTableReference, select);
                    var condition = join.SearchCondition is null ? string.Empty : GetFragmentText(join.SearchCondition);
                    select.Joins.Add(new ObjectShapeJoin(
                        FormatQualifiedJoinType(join.QualifiedJoinType),
                        right.Alias,
                        condition,
                        ExtractColumnRefs(join.SearchCondition),
                        $"{FormatQualifiedJoinType(join.QualifiedJoinType)} JOIN {right.DisplayText} ON {condition}"));
                    return right;

                case UnqualifiedJoin join:
                    ParseTableReference(join.FirstTableReference, select);
                    var unqualifiedRight = ParseTableReference(join.SecondTableReference, select);
                    var joinType = FormatUnqualifiedJoinType(join.UnqualifiedJoinType);
                    select.Joins.Add(new ObjectShapeJoin(
                        joinType,
                        unqualifiedRight.Alias,
                        string.Empty,
                        Array.Empty<ObjectShapeColumnRef>(),
                        $"{joinType} {unqualifiedRight.DisplayText}"));
                    return unqualifiedRight;

                case JoinParenthesisTableReference parenthesized:
                    return ParseTableReference(parenthesized.Join, select);

                case NamedTableReference named:
                    var source = ParseNamedSource(named);
                    if (!select.Sources.Any(existing => string.Equals(existing.Alias, source.Alias, StringComparison.OrdinalIgnoreCase)))
                    {
                        select.Sources.Add(source);
                    }

                    return source;

                case QueryDerivedTable derived:
                    var alias = derived.Alias?.Value ?? $"derived{select.Sources.Count + 1}";
                    var derivedSource = new ObjectShapeSource(alias, "<derived>", $"<derived> AS {alias}");
                    select.Sources.Add(derivedSource);
                    return derivedSource;

                default:
                    var text = GetFragmentText(tableReference);
                    var fallback = new ObjectShapeSource(text, text, text);
                    select.Sources.Add(fallback);
                    return fallback;
            }
        }

        private ObjectShapeSource ParseNamedSource(NamedTableReference node)
        {
            var objectName = FormatSchemaObjectName(node.SchemaObject);
            var alias = node.Alias?.Value ?? node.SchemaObject.Identifiers.Last().Value;
            var displayText = string.Equals(alias, objectName, StringComparison.OrdinalIgnoreCase)
                ? objectName
                : $"{objectName} AS {alias}";
            return new ObjectShapeSource(alias, objectName, displayText);
        }

        private IReadOnlyList<ObjectShapeColumnRef> ExtractColumnRefs(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return Array.Empty<ObjectShapeColumnRef>();
            }

            var visitor = new ColumnRefVisitor();
            fragment.Accept(visitor);
            return visitor.Refs;
        }

        private string GetFragmentText(TSqlFragment fragment)
        {
            var sb = new StringBuilder();
            for (var index = fragment.FirstTokenIndex; index <= fragment.LastTokenIndex; index++)
            {
                sb.Append(tokens[index].Text);
            }

            return sb.ToString().Trim();
        }

        private static QuerySpecification? TryGetQuerySpecification(QueryExpression queryExpression)
        {
            while (queryExpression is not null)
            {
                if (queryExpression is QuerySpecification spec)
                {
                    return spec;
                }

                if (queryExpression is QueryParenthesisExpression parenthesis)
                {
                    queryExpression = parenthesis.QueryExpression;
                    continue;
                }

                if (queryExpression is BinaryQueryExpression binary)
                {
                    queryExpression = binary.FirstQueryExpression;
                    continue;
                }

                break;
            }

            return null;
        }

        private static string FormatSchemaObjectName(SchemaObjectName name)
        {
            return string.Join(".", name.Identifiers.Select(identifier => $"[{identifier.Value}]"));
        }

        private static string FormatQualifiedJoinType(QualifiedJoinType joinType)
        {
            return joinType switch
            {
                QualifiedJoinType.Inner => "INNER",
                QualifiedJoinType.LeftOuter => "LEFT",
                QualifiedJoinType.RightOuter => "RIGHT",
                QualifiedJoinType.FullOuter => "FULL",
                _ => joinType.ToString().ToUpperInvariant()
            };
        }

        private static string FormatUnqualifiedJoinType(UnqualifiedJoinType joinType)
        {
            return joinType switch
            {
                UnqualifiedJoinType.CrossJoin => "CROSS JOIN",
                UnqualifiedJoinType.CrossApply => "CROSS APPLY",
                UnqualifiedJoinType.OuterApply => "OUTER APPLY",
                _ => joinType.ToString().ToUpperInvariant()
            };
        }
    }

    private sealed class ColumnRefVisitor : TSqlFragmentVisitor
    {
        public List<ObjectShapeColumnRef> Refs { get; } = new();

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;
            if (identifiers is null || identifiers.Count < 2)
            {
                return;
            }

            Refs.Add(new ObjectShapeColumnRef(identifiers[^2].Value, identifiers[^1].Value));
        }
    }
}

public sealed class ObjectShapeParseResult
{
    private ObjectShapeParseResult(bool isSuccess, string errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public string ErrorMessage { get; set; }

    public string TargetViewName { get; set; } = string.Empty;

    public ObjectShapeSelect Select { get; set; } = new();

    public static ObjectShapeParseResult Success()
    {
        return new ObjectShapeParseResult(true, string.Empty);
    }

    public static ObjectShapeParseResult Failed(string errorMessage)
    {
        return new ObjectShapeParseResult(false, errorMessage);
    }
}

public sealed class ObjectShapeSelect
{
    public List<ObjectShapeField> Fields { get; } = new();

    public List<ObjectShapeSource> Sources { get; } = new();

    public List<ObjectShapeJoin> Joins { get; } = new();

    public string WhereClauseText { get; set; } = string.Empty;

    public IReadOnlyList<ObjectShapeColumnRef> WhereColumnRefs { get; set; } = Array.Empty<ObjectShapeColumnRef>();
}

public sealed class ObjectShapeField
{
    public ObjectShapeField(
        string key,
        string outputName,
        string expressionText,
        string? sourceAlias,
        string? sourceColumn,
        bool hasExplicitAlias,
        IReadOnlyList<ObjectShapeColumnRef> columnRefs)
    {
        Key = key;
        OutputName = outputName;
        ExpressionText = expressionText;
        SourceAlias = sourceAlias;
        SourceColumn = sourceColumn;
        HasExplicitAlias = hasExplicitAlias;
        ColumnRefs = columnRefs;
    }

    public string Key { get; }

    public string OutputName { get; }

    public string ExpressionText { get; }

    public string? SourceAlias { get; }

    public string? SourceColumn { get; }

    public bool HasExplicitAlias { get; }

    public IReadOnlyList<ObjectShapeColumnRef> ColumnRefs { get; }

    public string Role => SourceAlias is null
        ? "Expression"
        : OutputName.EndsWith("Description", StringComparison.OrdinalIgnoreCase)
            ? "Lookup value"
            : "Field";

    public string ToSelectSql()
    {
        if (HasExplicitAlias || !string.Equals(OutputName, SourceColumn, StringComparison.OrdinalIgnoreCase))
        {
            return $"{ExpressionText} AS [{OutputName}]";
        }

        return ExpressionText;
    }
}

public sealed record ObjectShapeSource(string Alias, string ObjectName, string DisplayText);

public sealed record ObjectShapeJoin(
    string JoinType,
    string RightAlias,
    string ConditionText,
    IReadOnlyList<ObjectShapeColumnRef> ColumnRefs,
    string DisplayText);

public sealed record ObjectShapeColumnRef(string Alias, string Column);
