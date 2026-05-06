using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.Dialogs;
using SchemaStudioWebViewer.Components.Pages.ManageViews;
using SchemaStudioWebViewer.Utils;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViewsNext;

[AIChange("1.5", "2026-05-04 04:53 PM CDT allowed a new view's first parser column snapshot to save without requiring the merge dialog.", AICommandStatus.Pending)]
[AIChange("1.4", "2026-05-01 09:20 PM CDT allowed staged column synchronization rows to persist for any saved view instead of tying Save All Changes to the base-view manual metadata editor permission.", AICommandStatus.Pending)]
[AIChange("1.3", "2026-04-30 03:37 PM CDT added an Unknown-domain helper so the selector can render unclassified objects as flat items regardless of source casing.", AICommandStatus.Pending)]
[AIChange("1.2", "2026-04-30 03:31 PM CDT made Manage Views Next honor StringLength metadata for column edit limits and validate persisted text lengths before saving.", AICommandStatus.Pending)]
[AIChange("1.1", "2026-04-30 03:19 PM CDT extended the domain grouping model with Unknown detection and a flattened all-views collection for unclassified selector rendering.", AICommandStatus.Pending)]
[AIChange("1.0", "2026-04-30 03:37 PM CDT added shared state, labels, grouping, and save/delete helpers for the Manage Views Next prototype shell.", AICommandStatus.Pending)]
public partial class ManageViewsNext
{
    // 2026-04-30 03:37 PM CDT AI v1.3 manage-views-next marker: Unknown domain checks are centralized so unclassified objects never get base/composed subfolders.
    // 2026-04-30 03:31 PM CDT AI v1.2 manage-views-next marker: column edit controls and save validation now read StringLength limits from the persisted models.
    // 2026-04-30 03:19 PM CDT AI v1.1 manage-views-next marker: Unknown is a flat unclassified bucket, so the grouping model exposes AllViews and IsUnknown for the selector.
    // 2026-04-30 03:37 PM CDT AI v1.0 manage-views-next marker: shared prototype state is kept in a code-behind partial to preserve the split-file page pattern.
    private const string DefaultSourceCatalogDatabase = "VVGBI_Integrations";
    private const string UnknownDomain = "Unknown";
    private const int SelectorPanelDefaultWidth = 380;
    private const int SelectorPanelMinWidth = 280;
    private const int SelectorPanelMaxWidth = 560;
    private const int ViewDefinitionDefaultWidth = 490;
    private const int ViewDefinitionMinWidth = 270;
    private const int ViewDefinitionMaxWidth = 700;
    private const int ColumnListDefaultWidth = 390;
    private const int ColumnListMinWidth = 250;
    private const int ColumnListMaxWidth = 520;

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
    private ResizeTarget? ActiveResizeTarget;
    private double ResizeStartX;
    private int ResizeStartWidth;
    private int SelectorPanelWidth = SelectorPanelDefaultWidth;
    private int ViewDefinitionWidth = ViewDefinitionDefaultWidth;
    private int ColumnListWidth = ColumnListDefaultWidth;

    private enum WorkspaceResetLevel
    {
        View,
        Workspace
    }

    private enum ResizeTarget
    {
        Selector,
        ViewDefinition,
        ColumnList
    }

    private string ManageViewsNextClass =>
        ActiveResizeTarget == null
            ? "manage-views-next"
            : "manage-views-next mvn-resizing";

    private string PaneResizeStyle =>
        $"--mvn-selector-width: {SelectorPanelWidth}px; --mvn-view-definition-width: {ViewDefinitionWidth}px; --mvn-column-list-width: {ColumnListWidth}px;";

    private string CurrentSourceFullName =>
        EditableObject == null
            ? string.Empty
            : string.Join(".",
                new[] { EditableObject.SourceDatabaseName, EditableObject.SourceSchemaName, EditableObject.SourceObjectName }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));

    private bool IsViewDefinitionDirty => EditableObject?.IsDirty ?? false;
    private bool HasUnsavedColumnChanges =>
        SavedColumns.Any(column => column.IsDirty || IsPendingColumnMergeState(column.MergeState));
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

    private static bool IsPendingColumnMergeState(SchemaObjectColumnMergeState mergeState) =>
        mergeState is SchemaObjectColumnMergeState.PendingAdd
            or SchemaObjectColumnMergeState.PendingUpdate
            or SchemaObjectColumnMergeState.PendingRemove;

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
                .Select(domain =>
                {
                    var domainItems = ExistingViewItems
                        .Where(item => string.Equals(NormalizeDomain(item.Domain), domain, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new ViewDomainGroup(
                        domain,
                        string.Equals(domain, UnknownDomain, StringComparison.OrdinalIgnoreCase),
                        domainItems,
                        domainItems
                            .Where(item => item.IsBaseObject)
                            .ToList(),
                        domainItems
                            .Where(item => !item.IsBaseObject)
                            .ToList());
                })
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

    private void BeginPaneResize(ResizeTarget target, PointerEventArgs args)
    {
        ActiveResizeTarget = target;
        ResizeStartX = args.ClientX;
        ResizeStartWidth = target switch
        {
            ResizeTarget.Selector => SelectorPanelWidth,
            ResizeTarget.ViewDefinition => ViewDefinitionWidth,
            ResizeTarget.ColumnList => ColumnListWidth,
            _ => 0
        };
    }

    private void ResizePane(PointerEventArgs args)
    {
        if (ActiveResizeTarget == null)
        {
            return;
        }

        var nextWidth = ResizeStartWidth + (int)Math.Round(args.ClientX - ResizeStartX);

        switch (ActiveResizeTarget)
        {
            case ResizeTarget.Selector:
                SelectorPanelWidth = Math.Clamp(nextWidth, SelectorPanelMinWidth, SelectorPanelMaxWidth);
                break;
            case ResizeTarget.ViewDefinition:
                ViewDefinitionWidth = Math.Clamp(nextWidth, ViewDefinitionMinWidth, ViewDefinitionMaxWidth);
                break;
            case ResizeTarget.ColumnList:
                ColumnListWidth = Math.Clamp(nextWidth, ColumnListMinWidth, ColumnListMaxWidth);
                break;
        }
    }

    private void EndPaneResize()
    {
        ActiveResizeTarget = null;
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

        validationMessage = ValidateStringLengths(EditableObject, "View");
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Save blocked", validationMessage, 5000);
            return;
        }

        if (!await ConfirmSaveWithUnmergedParserChangesAsync())
        {
            return;
        }

        var columnsToSave = BuildColumnSaveSnapshot();

        validationMessage = ValidateStringLengths(columnsToSave, "Column");
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Save blocked", validationMessage, 5000);
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

            if (EditableObject.SchemaObjectId > 0 && columnsToSave.Count > 0)
            {
                foreach (var column in columnsToSave)
                {
                    column.SchemaObjectId = EditableObject.SchemaObjectId;
                }

                await SchemaObjectColumnRepository.SaveFullSnapshotAsync(columnsToSave);
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

    private List<SchemaObjectColumnDefinition> BuildColumnSaveSnapshot()
    {
        var includeDetectedAdds = ShouldSaveDetectedAddsAsInitialSnapshot();

        var sourceColumns = CloneColumns(SavedColumns)
            .Where(column => column.MergeState != SchemaObjectColumnMergeState.PendingRemove)
            .Where(column => includeDetectedAdds || column.MergeState != SchemaObjectColumnMergeState.DetectedAdd)
            .ToList();

        foreach (var column in sourceColumns)
        {
            column.MergeState = SchemaObjectColumnMergeState.None;
        }

        return sourceColumns;
    }

    private bool ShouldSaveDetectedAddsAsInitialSnapshot() =>
        EditableObject != null &&
        (EditableObject.SchemaObjectId <= 0 || OriginalSavedColumns.Count == 0) &&
        SavedColumns.Any(column => column.MergeState == SchemaObjectColumnMergeState.DetectedAdd);

    private async Task<bool> ConfirmSaveWithUnmergedParserChangesAsync()
    {
        var unmergedAdded = ShouldSaveDetectedAddsAsInitialSnapshot()
            ? 0
            : ReviewRows.Count(row => row.Status == "Added");
        var unmergedChanged = ReviewRows.Count(row => row.Status == "Changed");
        var unmergedRemoved = ReviewRows.Count(row => row.Status == "Removed");

        if (unmergedAdded == 0 && unmergedChanged == 0 && unmergedRemoved == 0)
        {
            return true;
        }

        var confirmed = await DialogService.OpenAsync<UnmergedParserChangesDialog>(
            "Parser Changes Not Applied",
            new Dictionary<string, object?>
            {
                ["Changed"] = unmergedChanged,
                ["Added"] = unmergedAdded,
                ["Removed"] = unmergedRemoved
            },
            new DialogOptions
            {
                Width = "430px",
                Resizable = false,
                Draggable = false,
                CloseDialogOnOverlayClick = false
            });

        return confirmed == true;
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

    private static string? ValidateStringLengths(object model, string labelPrefix)
    {
        foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string))
            {
                continue;
            }

            var maxLength = GetStringLength(property);
            if (maxLength is null)
            {
                continue;
            }

            var value = property.GetValue(model) as string;
            if (value?.Length > maxLength.Value)
            {
                return $"{labelPrefix} {ReflectionUtils.GetDisplayName(model.GetType(), property.Name)} is {value.Length} characters; max is {maxLength.Value}.";
            }
        }

        return null;
    }

    private static string? ValidateStringLengths(IEnumerable<SchemaObjectColumnDefinition> columns, string labelPrefix)
    {
        foreach (var column in columns)
        {
            var validationMessage = ValidateStringLengths(column, $"{labelPrefix} '{column.SourceColumnName}'");
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                return validationMessage;
            }
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

    private static bool IsUnknownDomain(string? domain) =>
        string.Equals(NormalizeDomain(domain), UnknownDomain, StringComparison.OrdinalIgnoreCase);

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
        var property = typeof(SchemaObjectColumnDefinition)
            .GetProperty(propertyName);

        return GetStringLength(property);
    }

    private static int? GetStringLength(PropertyInfo? property)
    {
        if (property == null)
        {
            return null;
        }

        var stringLength = property.GetCustomAttribute<StringLengthAttribute>();
        if (stringLength != null)
        {
            return stringLength.MaximumLength;
        }

        var maxLength = property.GetCustomAttribute<MaxLengthAttribute>();
        return maxLength?.Length;
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
        bool IsUnknown,
        List<ViewWorkspaceItem> AllViews,
        List<ViewWorkspaceItem> BaseViews,
        List<ViewWorkspaceItem> ComposedViews);
}
