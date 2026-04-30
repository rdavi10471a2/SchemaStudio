using System.Reflection;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.Pages.ManageViews;
using SchemaStudioWebViewer.Utils;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViewsNext;

[AIChange("1.0", "2026-04-30 03:37 PM CDT added shared state, labels, grouping, and save/delete helpers for the Manage Views Next prototype shell.", AICommandStatus.Pending)]
public partial class ManageViewsNext
{
    // 2026-04-30 03:37 PM CDT AI v1.0 manage-views-next marker: shared prototype state is kept in a code-behind partial to preserve the split-file page pattern.
    private const string DefaultSourceCatalogDatabase = "VVGBI_Integrations";
    private const string UnknownDomain = "Unknown";

    private List<DatabaseDefinition> Databases = new();
    private List<DatabaseDomainDefinition> Domains = new();
    private List<ViewWorkspaceItem> ExistingViewItems = new();
    private List<ViewWorkspaceItem> AvailableViewItems = new();
    private List<SchemaObjectColumnDefinition> SavedColumns = new();
    private List<SchemaObjectColumnDefinition> OriginalSavedColumns = new();
    private List<ManageViewsColumnReviewRow> ReviewRows = new();

    private int? SelectedDatabaseId;
    private string? SelectedViewKey;
    private string? SelectedDomainFilter;
    private string? LastAppliedDomainFilter;
    private bool IsLeftPanelHidden;
    private DatabaseDefinition? SelectedDatabase;
    private ViewWorkspaceItem? SelectedViewItem;
    private SchemaObjectDefinition? EditableObject;
    private ParsedQuery? CurrentParsedView;
    private bool IsBusy;
    private string LoadError = string.Empty;

    private enum WorkspaceResetLevel
    {
        View,
        Workspace
    }

    private string CurrentSourceFullName =>
        EditableObject == null
            ? string.Empty
            : string.Join(".",
                new[] { EditableObject.SourceDatabaseName, EditableObject.SourceSchemaName, EditableObject.SourceObjectName }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));

    private bool IsViewDefinitionDirty => EditableObject?.IsDirty ?? false;
    private bool HasUnsavedColumnChanges => SavedColumns.Any(column => column.IsDirty);
    private bool IsWorkspaceDirty => IsViewDefinitionDirty || HasUnsavedColumnChanges;
    private bool CanDeleteCurrentView => SelectedViewItem?.IsExisting == true && EditableObject?.SchemaObjectId > 0;
    private bool CanOpenDependencyTools => EditableObject != null;
    private bool CanEditColumns => EditableObject?.SchemaObjectId > 0;
    private bool CanEditSelectedColumnMetadata => EditableObject?.IsBaseObject == true;
    private string SelectedViewClassification =>
        SelectedViewItem?.IsExisting == false
            ? "Available"
            : EditableObject?.IsBaseObject == true
                ? "Base"
                : "Composed";

    private IReadOnlyList<string> ColumnSynchronizationProcessLines =>
    [
        "Reviews parser-detected added, changed, and removed columns for the selected view.",
        "Saves synchronization decisions separately from the view definition.",
        "Edit saved column business attributes from the editor above."
    ];

    private string ColumnSummarySentence
    {
        get
        {
            var added = ReviewRows.Count(x => x.Status == "Added");
            var changed = ReviewRows.Count(x => x.Status == "Changed");
            var removed = ReviewRows.Count(x => x.Status == "Removed");
            var unchanged = ReviewRows.Count(x => x.Status == "Unchanged");
            return $"{changed} changed, {added} added, {removed} removed, {unchanged} unchanged";
        }
    }

    private List<DatabaseDomainDefinition> LeftFilterDomains =>
        Domains
            .OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<ViewDomainGroup> DomainTreeGroups
    {
        get
        {
            var domainNames = Domains
                .Select(x => NormalizeDomain(x.Domain))
                .Append(UnknownDomain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => string.Equals(x, UnknownDomain, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(SelectedDomainFilter))
            {
                domainNames = domainNames
                    .Where(x => string.Equals(x, SelectedDomainFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return domainNames
                .Select(domain => new ViewDomainGroup(
                    domain,
                    ExistingViewItems
                        .Where(item => item.IsBaseObject)
                        .Where(item => string.Equals(NormalizeDomain(item.Domain), domain, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    ExistingViewItems
                        .Where(item => !item.IsBaseObject)
                        .Where(item => string.Equals(NormalizeDomain(item.Domain), domain, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .Where(group => group.BaseViews.Count > 0 || group.ComposedViews.Count > 0 || string.Equals(group.Domain, UnknownDomain, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadDatabasesAsync(null);
    }

    private void ToggleLeftPanel()
    {
        IsLeftPanelHidden = !IsLeftPanelHidden;
    }

    private async Task SaveViewAsync()
    {
        if (EditableObject == null || SelectedDatabaseId == null)
        {
            return;
        }

        var validationMessage = ValidateEditableObject(EditableObject);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Save blocked", validationMessage, 4000);
            return;
        }

        IsBusy = true;

        try
        {
            EditableObject.DatabaseId = SelectedDatabaseId.Value;
            EditableObject.SourceDatabaseName ??= SelectedViewItem?.SourceDatabaseName;
            EditableObject.IsActive = true;

            if (EditableObject.SchemaObjectId == 0)
            {
                var newId = await SchemaObjectRepository.CreateAsync(EditableObject);
                SelectedViewKey = BuildExistingSelectionKey(newId);
            }
            else
            {
                await SchemaObjectRepository.UpdateAsync(EditableObject);
                SelectedViewKey = BuildExistingSelectionKey(EditableObject.SchemaObjectId);
            }

            if (EditableObject.SchemaObjectId > 0 && CanEditSelectedColumnMetadata && SavedColumns.Any(column => column.IsDirty))
            {
                await SchemaObjectColumnRepository.SaveAllAsync(SavedColumns);
            }

            NotificationService.Notify(NotificationSeverity.Success, "View saved", "View and column metadata were saved successfully.", 2500);
            await LoadWorkspaceAsync(SelectedDatabaseId.Value, SelectedViewKey);
        }
        catch (Exception ex)
        {
            NotifyFailure("Save failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteViewAsync()
    {
        if (!CanDeleteCurrentView || EditableObject == null || SelectedDatabaseId == null)
        {
            return;
        }

        var confirmed = await DialogService.Confirm(
            "Delete this view definition? Column records tied to this imported view will also be removed.",
            "Delete View",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });

        if (confirmed != true)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await SchemaObjectRepository.DeleteAsync(EditableObject.SchemaObjectId);
            NotificationService.Notify(NotificationSeverity.Success, "View deleted", "The saved view definition was removed.", 2500);
            ResetWorkspace(WorkspaceResetLevel.View);
            await LoadWorkspaceAsync(SelectedDatabaseId.Value, null, autoSelectFirst: false);
        }
        catch (Exception ex)
        {
            NotifyFailure("Delete failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string GetTreeItemClass(ViewWorkspaceItem item) =>
        string.Equals(SelectedViewKey, item.SelectionKey, StringComparison.Ordinal)
            ? "mvn-tree-item selected"
            : "mvn-tree-item";

    private SchemaObjectDefinition CreateNewObjectDraft(ViewWorkspaceItem item)
    {
        var draft = new SchemaObjectDefinition
        {
            DatabaseId = SelectedDatabaseId ?? 0,
            SourceDatabaseName = item.SourceDatabaseName,
            SourceSchemaName = item.SourceSchemaName,
            SourceObjectName = item.SourceObjectName,
            Domain = GetPreferredDomain(),
            IsActive = true
        };

        draft.ClearDirty();
        return draft;
    }

    private static SchemaObjectDefinition CloneObject(SchemaObjectDefinition source)
    {
        var clone = new SchemaObjectDefinition
        {
            SchemaObjectId = source.SchemaObjectId,
            DatabaseId = source.DatabaseId,
            SourceDatabaseName = source.SourceDatabaseName,
            SourceSchemaName = source.SourceSchemaName,
            SourceObjectName = source.SourceObjectName,
            IsBaseObject = source.IsBaseObject,
            Domain = source.Domain,
            BusinessName = source.BusinessName,
            BusinessDescription = source.BusinessDescription,
            DeveloperNotes = source.DeveloperNotes,
            IsActive = source.IsActive,
            LastSynced = source.LastSynced
        };

        clone.ClearDirty();
        return clone;
    }

    private static string? ValidateEditableObject(SchemaObjectDefinition model)
    {
        if (string.IsNullOrWhiteSpace(model.SourceSchemaName) || string.IsNullOrWhiteSpace(model.SourceObjectName))
        {
            return "Source schema and object are required.";
        }

        if (string.IsNullOrWhiteSpace(model.BusinessName))
        {
            return "Business Name is required.";
        }

        if (string.IsNullOrWhiteSpace(model.Domain))
        {
            return "Select a domain for the view.";
        }

        return null;
    }

    private string? GetPreferredDomain()
    {
        var unknown = Domains.FirstOrDefault(x =>
            string.Equals(x.Domain, UnknownDomain, StringComparison.OrdinalIgnoreCase));

        return unknown?.Domain ?? Domains.FirstOrDefault()?.Domain;
    }

    private string ResolveSourceCatalogDatabase(IEnumerable<SchemaObjectDefinition> existingObjects)
    {
        var establishedSourceDatabase = existingObjects
            .Select(x => x.SourceDatabaseName)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        if (!string.IsNullOrWhiteSpace(establishedSourceDatabase))
        {
            return establishedSourceDatabase;
        }

        return DefaultSourceCatalogDatabase;
    }

    private static string NormalizeDomain(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? UnknownDomain : domain.Trim();

    private RenderFragment FieldLabel(Type modelType, string propertyName) => builder =>
    {
        var displayName = ReflectionUtils.GetDisplayName(modelType, propertyName);
        var description = ReflectionUtils.GetDescription(modelType, propertyName);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "mvn-field-label");
        builder.AddContent(2, displayName);

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.OpenComponent<RadzenIcon>(3);
            builder.AddAttribute(4, "Icon", "help_outline");
            builder.AddAttribute(5, "MouseEnter", EventCallback.Factory.Create<ElementReference>(this, args => TooltipService.Open(args, description)));
            builder.CloseComponent();
        }

        builder.CloseElement();
    };

    private RenderFragment ColumnFieldLabel(string propertyName) => FieldLabel(typeof(SchemaObjectColumnDefinition), propertyName);

    private RenderFragment MetadataHelp(string propertyName) => builder =>
    {
        var description = ReflectionUtils.GetDescription(typeof(SchemaObjectColumnDefinition), propertyName);
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        builder.OpenComponent<RadzenIcon>(0);
        builder.AddAttribute(1, "Icon", "help_outline");
        builder.AddAttribute(2, "MouseEnter", EventCallback.Factory.Create<ElementReference>(this, args => TooltipService.Open(args, description)));
        builder.CloseComponent();
    };

    private static int? GetMaxLength(string propertyName)
    {
        var attribute = typeof(SchemaObjectColumnDefinition)
            .GetProperty(propertyName)?
            .GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();

        return attribute?.Length;
    }

    private void NotifyFailure(string summary, Exception ex)
    {
        LoadError = ex.Message;
        NotificationService.Notify(NotificationSeverity.Error, summary, ex.Message, 5000);
    }

    private sealed class ViewWorkspaceItem
    {
        public string SelectionKey { get; set; } = string.Empty;
        public bool IsExisting { get; set; }
        public int? SchemaObjectId { get; set; }
        public bool IsBaseObject { get; set; }
        public string SourceDatabaseName { get; set; } = string.Empty;
        public string SourceSchemaName { get; set; } = string.Empty;
        public string SourceObjectName { get; set; } = string.Empty;
        public string? Domain { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
    }

    private sealed record ViewDomainGroup(
        string Domain,
        List<ViewWorkspaceItem> BaseViews,
        List<ViewWorkspaceItem> ComposedViews);
}
