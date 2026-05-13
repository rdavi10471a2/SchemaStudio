using System.Text;
using System.Text.RegularExpressions;
using Radzen;
using SchemaStudioWebViewer.WEBSemanticModel.Parsing;

[module: SchemaStudio.AIHelpers.AIFileContext(
    "Components/Pages/DomainObjectModeler/DomainObjectModeler.Sql.cs",
    "SQL generation and validation slice for the Domain Object Modeler page.",
    Responsibilities = "Fetch selected base-view SQL, optionally strip internal comments, assemble CREATE OR ALTER VIEW CTE SQL, validate parser compatibility, and quote identifiers.",
    RelatedFiles = "Components/Pages/DomainObjectModeler/DomainObjectModeler.razor; Repositories/ReadOnlyViewDefinitionRepository.cs",
    LastReviewed = "2026-05-13")]

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private async Task GenerateSqlAsync()
    {
        if (!CanGenerate)
        {
            StatusMessage = "Choose an anchor and complete every join clause before generating SQL.";
            return;
        }

        IsBusy = true;

        try
        {
            var anchor = AnchorView!;
            var selected = SelectedBaseViews.ToList();
            var builder = new StringBuilder();

            builder.AppendLine($"CREATE OR ALTER VIEW {QualifiedName(TargetSchema, TargetViewName)}");
            builder.AppendLine("AS");
            builder.AppendLine("WITH");

            for (var index = 0; index < selected.Count; index++)
            {
                var item = selected[index];
                var definition = await ReadOnlyViewDefinitionRepository.GetViewDefinitionAsync(
                    item.Source.SourceDatabaseName ?? SelectedDatabaseName(),
                    item.Source.SourceSchemaName,
                    item.Source.SourceObjectName);

                var sql = definition?.Definition ?? $"SELECT * FROM {QualifiedName(item.Source.SourceDatabaseName, item.Source.SourceSchemaName, item.Source.SourceObjectName)}";
                if (StripSourceComments)
                {
                    sql = ReadOnlyViewDefinitionRepository.CleanSqlDefinition(sql);
                }

                sql = ExtractSelectableSql(sql);

                builder.AppendLine($"    {QuoteIdentifier(item.AliasName)} AS");
                builder.AppendLine("    (");
                foreach (var line in sql.SplitLines())
                {
                    builder.AppendLine($"        {line}");
                }

                builder.AppendLine(index == selected.Count - 1 ? "    )" : "    ),");
            }

            builder.AppendLine("SELECT");
            for (var index = 0; index < selected.Count; index++)
            {
                var suffix = index == selected.Count - 1 ? string.Empty : ",";
                builder.AppendLine($"    {QuoteIdentifier(selected[index].AliasName)}.*{suffix}");
            }

            builder.AppendLine($"FROM {QuoteIdentifier(anchor.AliasName)}");

            foreach (var item in NonAnchorSelectedBaseViews)
            {
                var row = FindJoinRow(item.SchemaObjectId);
                if (row is null || string.IsNullOrWhiteSpace(row.OnClause))
                {
                    continue;
                }

                builder.AppendLine($"{row.JoinType} {QuoteIdentifier(item.AliasName)}");
                builder.AppendLine($"    ON {row.OnClause}");
            }

            builder.AppendLine(";");

            GeneratedSql = builder.ToString();
            StatusMessage = StripSourceComments
                ? "Generated SQL with internal source comments removed."
                : "Generated SQL from current anchor, names, and join rows.";
        }
        catch (Exception ex)
        {
            NotifyFailure("SQL generation failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ValidateSql()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql))
        {
            StatusMessage = "Generate SQL before validating.";
            return;
        }

        try
        {
            var parser = new ViewParser();
            var result = parser.Parse(GeneratedSql);
            StatusMessage = result is not null
                ? "Parser validation completed."
                : "Parser validation returned no result.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Parser validation failed: {ex.Message}";
            NotificationService.Notify(NotificationSeverity.Error, "Validation failed", ex.Message, 6000);
        }
    }

    private string SelectedDatabaseName() =>
        Databases.FirstOrDefault(database => database.DatabaseId == SelectedDatabaseId)?.DatabaseName ?? string.Empty;

    private static string ExtractSelectableSql(string sql)
    {
        var normalized = sql.Trim().TrimEnd(';').Trim();
        var match = Regex.Match(
            normalized,
            @"\bAS\b\s*(?<body>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success
            ? match.Groups["body"].Value.Trim()
            : normalized;
    }

    private static string QualifiedName(params string?[] parts) =>
        string.Join(".", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => QuoteIdentifier(part!)));

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}

internal static class DomainObjectModelerStringExtensions
{
    public static IEnumerable<string> SplitLines(this string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}

