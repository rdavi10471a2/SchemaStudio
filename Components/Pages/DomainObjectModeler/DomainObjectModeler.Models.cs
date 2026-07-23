using SchemaStudio.Data.Models;

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private sealed class DomainBaseViewItem
    {
        public required SchemaObjectDefinition Source { get; init; }
        public bool IsSelected { get; set; }
        public int SelectionOrdinal { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string RelationshipTableName { get; set; } = string.Empty;
        public bool IsAnchor { get; set; }
        public string CteSourceMode { get; set; } = CteSourceModeInlineContents;

        public int SchemaObjectId => Source.SchemaObjectId;
        public string DisplayName => string.IsNullOrWhiteSpace(Source.BusinessName)
            ? Source.SourceObjectName
            : Source.BusinessName!;
        public string SourceFullName => string.Join(".",
            new[] { Source.SourceDatabaseName, Source.SourceSchemaName, Source.SourceObjectName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private sealed class DomainObjectJoinRow
    {
        public int SchemaObjectId { get; init; }
        public string JoinType { get; set; } = "LEFT JOIN";
        public string OnClause { get; set; } = string.Empty;
        public bool IsInferred { get; set; }
    }

    private sealed record CteSourceModeOption(string Value, string Text);
}

