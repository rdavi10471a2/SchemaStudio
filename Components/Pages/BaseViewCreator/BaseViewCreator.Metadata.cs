using System.Reflection;
using Microsoft.AspNetCore.Components;
using Radzen.Blazor;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Data;
using SchemaStudioWebViewer.Models;
using SchemaStudioWebViewer.Utils;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

public sealed record BaseViewCreatorMetadataEditResult(string BusinessName, string BusinessDescription);

public partial class BaseViewCreator
{
    private void OpenColumnMetadataDialog(TableSchemaColumnInfo column)
    {
        MetadataEditColumn = column;
        MetadataEditRelationship = null;
        MetadataDialogTitle = $"Metadata for {column.ColumnName}";

        // Seed with the same effective value the projection tree shows: the user's own edit when
        // present, otherwise the comment imported from the existing view. FK-ref columns are keyed
        // by their {col}_FK output alias; base columns by their column name.
        var outputAlias = RelationshipOwnsEditorColumn(column.ColumnName)
            ? $"{column.ColumnName}_FK"
            : column.ColumnName;
        var (businessName, businessDescription) = ApplyImportedComment(outputAlias, column.BusinessName, column.BusinessDescription);
        MetadataEditBusinessName = businessName;
        MetadataEditBusinessDescription = businessDescription;
        MetadataDialogOpen = true;
    }

    private void OpenRelationshipDisplayMetadataDialog(TableSchemaRelationshipInfo relationship)
    {
        MetadataEditColumn = null;
        MetadataEditRelationship = relationship;
        var lookupAlias = BuildLookupProjectionAlias(relationship);
        MetadataDialogTitle = $"Metadata for {lookupAlias}";

        // Seed with the effective value shown in the tree: user edit if present, else imported comment.
        var (businessName, businessDescription) = ApplyImportedComment(lookupAlias, relationship.DisplayBusinessName, relationship.DisplayBusinessDescription);
        MetadataEditBusinessName = businessName;
        MetadataEditBusinessDescription = businessDescription;
        MetadataDialogOpen = true;
    }

    private void CloseMetadataDialog()
    {
        MetadataDialogOpen = false;
        MetadataEditColumn = null;
        MetadataEditRelationship = null;
    }

    private void ApplyMetadataDialog(BaseViewCreatorMetadataEditResult editResult)
    {
        if (!ValidateMetadataDialog(editResult))
        {
            return;
        }

        MetadataEditBusinessName = editResult.BusinessName;
        MetadataEditBusinessDescription = editResult.BusinessDescription;

        if (MetadataEditColumn is not null)
        {
            MetadataEditColumn.BusinessName = MetadataEditBusinessName;
            MetadataEditColumn.BusinessDescription = MetadataEditBusinessDescription;
        }
        else if (MetadataEditRelationship is not null)
        {
            MetadataEditRelationship.DisplayBusinessName = MetadataEditBusinessName;
            MetadataEditRelationship.DisplayBusinessDescription = MetadataEditBusinessDescription;
        }

        MarkSqlDirty();
        CloseMetadataDialog();
    }

    private bool ValidateMetadataDialog(BaseViewCreatorMetadataEditResult editResult)
    {
        if (editResult.BusinessName.Length > MetadataBusinessNameMaxLength)
        {
            NotifyError($"Business Name is {editResult.BusinessName.Length} characters; max is {MetadataBusinessNameMaxLength}.");
            return false;
        }

        if (editResult.BusinessDescription.Length > MetadataBusinessDescriptionMaxLength)
        {
            NotifyError($"Business Description is {editResult.BusinessDescription.Length} characters; max is {MetadataBusinessDescriptionMaxLength}.");
            return false;
        }

        return true;
    }

    private string BuildMetadataPlaceholder(string prefix, string projection, string outputColumnName, string businessName = "", string businessDescription = "", bool disableInheritance = false)
    {
        businessName = string.IsNullOrWhiteSpace(businessName) ? "--Not Specified--" : businessName;
        businessDescription = string.IsNullOrWhiteSpace(businessDescription) ? "--Not Specified--" : businessDescription;

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(businessName))
        {
            parts.Add($"@BusinessName: {businessName}");
        }

        if (!string.IsNullOrWhiteSpace(businessDescription))
        {
            parts.Add($"@BusinessDescription: {WrapText(businessDescription, MetadataDescriptionWrapWidth)}");
        }

        if (disableInheritance)
        {
            parts.Add("@DisableInheritance: True");
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        return BuildMultilineMetadataPlaceholder(prefix, projection, parts);
    }

    private static string BuildMultilineMetadataPlaceholder(string prefix, string projection, IReadOnlyList<string> parts)
    {
        var projectionLeadLength = prefix.Length + projection.Length;
        var tagIndent = new string(' ', projectionLeadLength + 4);
        var closeIndent = new string(' ', projectionLeadLength + 1);
        var builder = new System.Text.StringBuilder();
        builder.Append($" /* {AlignPartLines(parts[0], tagIndent)}");

        for (var index = 1; index < parts.Count; index++)
        {
            builder.AppendLine();
            builder.Append(tagIndent);
            builder.Append(AlignPartLines(parts[index], tagIndent));
        }

        builder.AppendLine();
        builder.Append(closeIndent);
        builder.Append("*/");
        return builder.ToString();
    }

    // Keeps a multi-line part (e.g. a @BusinessDescription with embedded line breaks) aligned inside the
    // /* */ block by indenting its wrapped lines to the same column as the leading tag.
    private static string AlignPartLines(string part, string indent) =>
        part.Replace("\r\n", "\n").Replace("\n", System.Environment.NewLine + indent);

    // Target text width (characters) for a wrapped @BusinessDescription line, before the tag indent.
    private const int MetadataDescriptionWrapWidth = 80;

    // Collapses any authored/round-tripped whitespace (line breaks, runs of spaces, indentation) and
    // greedily word-wraps to lines no wider than width, so generated comments read consistently no matter
    // how the description was entered. Applied only at comment-generation time -- the stored description
    // (and its MaxLength) is untouched.
    private static string WrapText(string text, int width)
    {
        var words = text.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return text.Trim();
        }

        var builder = new System.Text.StringBuilder();
        var lineLength = 0;
        foreach (var word in words)
        {
            if (lineLength == 0)
            {
                builder.Append(word);
                lineLength = word.Length;
            }
            else if (lineLength + 1 + word.Length > width)
            {
                builder.Append(System.Environment.NewLine).Append(word);
                lineLength = word.Length;
            }
            else
            {
                builder.Append(' ').Append(word);
                lineLength += 1 + word.Length;
            }
        }

        return builder.ToString();
    }

    private RenderFragment MetadataFieldLabel(string propertyName) => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "bvg-field-label");
        builder.AddContent(2, ReflectionUtils.GetDisplayName(typeof(SchemaObjectColumnDefinition), propertyName));
        builder.AddContent(3, MetadataHelp(typeof(SchemaObjectColumnDefinition), propertyName));
        builder.CloseElement();
    };

    private RenderFragment MetadataHelp(Type modelType, string propertyName) => builder =>
    {
        var description = ReflectionUtils.GetDescription(modelType, propertyName);
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        builder.OpenComponent<RadzenIcon>(0);
        builder.AddAttribute(1, "Icon", "help_outline");
        builder.AddAttribute(2, "class", "bvg-help-icon");
        builder.AddAttribute(3, "MouseEnter", EventCallback.Factory.Create<ElementReference>(this, args => TooltipService.Open(args, description)));
        builder.CloseComponent();
    };

    private RenderFragment MetadataSummary(string? businessName, string? businessDescription) => builder =>
    {
        var hasName = !string.IsNullOrWhiteSpace(businessName);
        var hasDescription = !string.IsNullOrWhiteSpace(businessDescription);

        builder.OpenElement(1, "span");
        builder.AddAttribute(2, "class", hasName || hasDescription ? "bvg-metadata-summary" : "bvg-metadata-summary empty");

        if (hasName)
        {
            builder.OpenElement(3, "span");
            builder.AddContent(4, businessName);
            builder.CloseElement();
        }

        if (hasDescription)
        {
            builder.OpenElement(5, "span");
            builder.AddContent(6, businessDescription);
            builder.CloseElement();
        }

        if (!hasName && !hasDescription)
        {
            builder.AddContent(7, "No metadata");
        }

        builder.CloseElement();
    };
}
