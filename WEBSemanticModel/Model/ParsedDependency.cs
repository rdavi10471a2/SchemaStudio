using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchemaStudioWebViewer.WEBSemanticModel.Model;

public sealed class ParsedDependency
{
    [Display(Name = "Kind", Order = 10)]
    [Description("Parser-resolved dependency kind. Views have a parsed child query; tables are terminal named objects.")]
    public string ObjectKind { get; set; } = "";

    [Display(Name = "Database", Order = 20)]
    [Description("Database resolved by the parser walk.")]
    public string Database { get; set; } = "";

    [Display(Name = "Schema", Order = 30)]
    [Description("Schema resolved by the parser walk.")]
    public string Schema { get; set; } = "";

    [Display(Name = "Object", Order = 40)]
    [Description("Object name resolved by the parser walk.")]
    public string ObjectName { get; set; } = "";

    [Display(Name = "Alias", Order = 50)]
    [Description("Alias used at the point where this dependency was encountered.")]
    public string? Alias { get; set; }

    [Display(Name = "Depth", Order = 60)]
    [Description("Distance from the root parsed view.")]
    public int Depth { get; set; }

    [Display(Name = "Resolved Name", Order = 70)]
    [Description("Fully qualified parser-resolved dependency name.")]
    public string ResolvedName =>
        string.Join(".",
            new[] { Database, Schema, ObjectName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
}

public static class ParsedDependencyExtensions
{
    public static List<ParsedDependency> GetResolvedDependencies(this ParsedQuery? query)
    {
        var dependencies = new List<ParsedDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (query == null)
        {
            return dependencies;
        }

        AddResolvedDependencies(query, dependencies, seen, depth: 1);
        return dependencies;
    }

    private static void AddResolvedDependencies(
        ParsedQuery query,
        List<ParsedDependency> dependencies,
        HashSet<string> seen,
        int depth)
    {
        foreach (var source in query.SourceTables)
        {
            if (source.NestedQuery != null)
            {
                if (source.Kind != SourceKind.DerivedQuery)
                {
                    AddNamedDependency(source, dependencies, seen, depth, "View");
                }

                AddResolvedDependencies(source.NestedQuery, dependencies, seen, depth + 1);
                continue;
            }

            if (source.Kind != SourceKind.DerivedQuery)
            {
                AddNamedDependency(source, dependencies, seen, depth, "Table");
            }
        }
    }

    private static void AddNamedDependency(
        SourceTable source,
        List<ParsedDependency> dependencies,
        HashSet<string> seen,
        int depth,
        string objectKind)
    {
        if (string.IsNullOrWhiteSpace(source.Table))
        {
            return;
        }

        var database = source.Database ?? "";
        var schema = source.Schema ?? "";
        var objectName = source.Table;
        var key = $"{database}.{schema}.{objectName}";

        if (!seen.Add(key))
        {
            return;
        }

        dependencies.Add(new ParsedDependency
        {
            ObjectKind = objectKind,
            Database = database,
            Schema = schema,
            ObjectName = objectName,
            Alias = source.Alias,
            Depth = depth
        });
    }
}
