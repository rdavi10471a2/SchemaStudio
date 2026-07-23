using Microsoft.AspNetCore.Components;

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private async Task SyncTargetInputsAsync()
    {
        TargetSchema = await JSRuntime.InvokeAsync<string>("domModeler.value", new object?[] { "dom-modeler-target-schema" });
        TargetViewName = await JSRuntime.InvokeAsync<string>("domModeler.value", new object?[] { "dom-modeler-target-view" });
    }

    private void OnAliasInput(DomainBaseViewItem item, ChangeEventArgs args)
    {
        OnAliasChanged(item, args.Value?.ToString());
    }

    private void OnAliasChanged(DomainBaseViewItem item, string? value)
    {
        item.AliasName = SanitizeAlias(value);
        GeneratedSql = string.Empty;
    }

    private void OnJoinTypeChanged(DomainObjectJoinRow row, object? value)
    {
        row.JoinType = value?.ToString() ?? "LEFT JOIN";
        row.IsInferred = false;
        GeneratedSql = string.Empty;
    }

    private void OnJoinClauseChanged(DomainObjectJoinRow row, string? value)
    {
        row.OnClause = value?.Trim() ?? string.Empty;
        row.IsInferred = false;
        GeneratedSql = string.Empty;
    }

    private void OnJoinClauseInput(DomainObjectJoinRow row, ChangeEventArgs args)
    {
        row.OnClause = args.Value?.ToString() ?? string.Empty;
        row.IsInferred = false;
        GeneratedSql = string.Empty;
    }

    private void OnCteSourceModeChanged(DomainBaseViewItem item, string? value)
    {
        item.CteSourceMode = value is CteSourceModeViewSurface ? CteSourceModeViewSurface : CteSourceModeInlineContents;
        GeneratedSql = string.Empty;
    }

    private async Task RemoveCommentsAsync()
    {
        StripSourceComments = true;
        StatusMessage = "Internal source comments will be removed from generated CTE blocks.";

        if (GeneratedSql.Length > 0)
        {
            await GenerateSqlAsync();
        }
    }

    private static string SanitizeAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "SourceObject";
        }

        var trimmed = value.Trim();
        var chars = trimmed
            .Where(character => char.IsLetterOrDigit(character) || character == '_')
            .ToArray();

        return chars.Length == 0 ? "SourceObject" : new string(chars);
    }
}
