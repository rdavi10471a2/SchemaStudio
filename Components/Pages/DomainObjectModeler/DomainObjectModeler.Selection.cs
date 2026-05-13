using Microsoft.AspNetCore.Components;

[module: SchemaStudio.AIHelpers.AIFileContext(
    "Components/Pages/DomainObjectModeler/DomainObjectModeler.Selection.cs",
    "Selection and loading workflow for the Domain Object Modeler page.",
    Responsibilities = "Load databases, domains, and domain-filtered base views, then keep selection, anchor, alias, and join-row state coherent.",
    RelatedFiles = "Components/Pages/DomainObjectModeler/DomainObjectModeler.razor; SchemaStudio.Data/Repositories/SchemaObjectRepository.cs",
    LastReviewed = "2026-05-13")]

namespace SchemaStudioWebViewer.Components.Pages.DomainObjectModeler;

public partial class DomainObjectModeler
{
    private async Task LoadDatabasesAsync()
    {
        IsBusy = true;
        LoadError = string.Empty;

        try
        {
            Databases = (await DatabaseRepository.GetAllAsync())
                .Where(database => database.Active)
                .OrderBy(database => database.DatabaseName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StatusMessage = Databases.Count == 0
                ? "No active databases are configured."
                : "Select a database and domain to begin.";
        }
        catch (Exception ex)
        {
            NotifyFailure("Domain Object Modeler load failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnDatabaseChanged(object? value)
    {
        SelectedDatabaseId = value is int id ? id : null;
        SelectedDomain = string.Empty;
        Domains.Clear();
        BaseViews.Clear();
        JoinRows.Clear();
        GeneratedSql = string.Empty;

        if (!SelectedDatabaseId.HasValue)
        {
            StatusMessage = "Select a database and domain to begin.";
            return;
        }

        IsBusy = true;

        try
        {
            Domains = (await DatabaseDomainRepository.GetByDatabaseIdAsync(SelectedDatabaseId.Value))
                .OrderBy(domain => domain.Domain, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StatusMessage = Domains.Count == 0
                ? "No domains are configured for the selected database."
                : "Select a domain to load base views.";
        }
        catch (Exception ex)
        {
            NotifyFailure("Domain load failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnDomainChanged(object? value)
    {
        SelectedDomain = value?.ToString() ?? string.Empty;
        await LoadBaseViewsAsync();
    }

    private async Task RefreshCatalogAsync()
    {
        if (SelectedDatabaseId.HasValue && !string.IsNullOrWhiteSpace(SelectedDomain))
        {
            await LoadBaseViewsAsync();
            return;
        }

        await LoadDatabasesAsync();
    }

    private async Task LoadBaseViewsAsync()
    {
        BaseViews.Clear();
        JoinRows.Clear();
        GeneratedSql = string.Empty;

        if (!SelectedDatabaseId.HasValue || string.IsNullOrWhiteSpace(SelectedDomain))
        {
            StatusMessage = "Select a database and domain to load base views.";
            return;
        }

        IsBusy = true;

        try
        {
            var rows = await SchemaObjectRepository.GetBaseObjectsByDatabaseAndDomainAsync(
                SelectedDatabaseId.Value,
                SelectedDomain);

            BaseViews = rows
                .Select(source => new DomainBaseViewItem
                {
                    Source = source,
                    AliasName = BuildDefaultAlias(source.SourceObjectName)
                })
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            TargetViewName = string.IsNullOrWhiteSpace(SelectedDomain)
                ? TargetViewName
                : $"v{SanitizeIdentifierToken(SelectedDomain)}EditableObject";

            StatusMessage = BaseViews.Count == 0
                ? $"No active base views are registered for {SelectedDomain}."
                : $"Loaded {BaseViews.Count} base views for {SelectedDomain}.";
        }
        catch (Exception ex)
        {
            NotifyFailure("Base view load failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ToggleBaseView(DomainBaseViewItem item, bool isSelected)
    {
        item.IsSelected = isSelected;
        GeneratedSql = string.Empty;

        if (!item.IsSelected)
        {
            item.IsAnchor = false;
            JoinRows.RemoveAll(row => row.SchemaObjectId == item.SchemaObjectId);
        }
        else if (AnchorView is null)
        {
            SetAnchor(item);
        }

        EnsureJoinRows();
    }

    private void SetAnchor(DomainBaseViewItem item)
    {
        if (!item.IsSelected)
        {
            item.IsSelected = true;
        }

        foreach (var baseView in BaseViews)
        {
            baseView.IsAnchor = ReferenceEquals(baseView, item);
        }

        GeneratedSql = string.Empty;
        EnsureJoinRows();
    }

    private void EnsureJoinRows()
    {
        var requiredIds = NonAnchorSelectedBaseViews
            .Select(item => item.SchemaObjectId)
            .ToHashSet();

        JoinRows.RemoveAll(row => !requiredIds.Contains(row.SchemaObjectId));

        foreach (var item in NonAnchorSelectedBaseViews)
        {
            if (FindJoinRow(item.SchemaObjectId) is null)
            {
                JoinRows.Add(new DomainObjectJoinRow
                {
                    SchemaObjectId = item.SchemaObjectId,
                    OnClause = BuildDefaultOnClause(item)
                });
            }
        }
    }

    private DomainObjectJoinRow? FindJoinRow(int schemaObjectId) =>
        JoinRows.FirstOrDefault(row => row.SchemaObjectId == schemaObjectId);

    private DomainBaseViewItem? FindBaseView(int schemaObjectId) =>
        BaseViews.FirstOrDefault(item => item.SchemaObjectId == schemaObjectId);

    private static string BuildDefaultAlias(string sourceObjectName)
    {
        var token = SanitizeIdentifierToken(sourceObjectName);
        if (token.StartsWith("bv", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
        {
            token = token[2..];
        }

        return string.IsNullOrWhiteSpace(token) ? "SourceObject" : $"{token}Source";
    }

    private string BuildDefaultOnClause(DomainBaseViewItem item)
    {
        var anchor = AnchorView;
        if (anchor is null)
        {
            return string.Empty;
        }

        var keyName = $"{SanitizeIdentifierToken(SelectedDomain)}Key";
        return $"[{anchor.AliasName}].[{keyName}] = [{item.AliasName}].[{keyName}]";
    }

    private static string SanitizeIdentifierToken(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return chars.Length == 0 ? "Object" : new string(chars);
    }
}

