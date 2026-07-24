using System.IO;
using System.Text.Json;
using SchemaStudio.Data.Models;
using SchemaStudio.Data.Repositories;

namespace RelationshipLoader;

public sealed class MainForm : Form
{
    private readonly AppConfig _config;

    private readonly TextBox _sourceConfigPathBox = new();
    private readonly Button _readFromSourceButton = new();
    private readonly ComboBox _dbCombo = new();
    private readonly Button _loadDbsButton = new();
    private readonly TextBox _schemaStudioBox = new();
    private readonly TextBox _excedeBox = new();
    private readonly CheckBox _lookupsCheck = new();
    private readonly CheckBox _childrenCheck = new();
    private readonly CheckBox _colookupCheck = new();
    private readonly CheckBox _ignoreSelfJoinsCheck = new();
    private readonly Button _saveConfigButton = new();
    private readonly Button _previewButton = new();
    private readonly Button _loadButton = new();
    private readonly TextBox _logBox = new();

    public MainForm()
    {
        _config = AppConfig.Load();

        Text = "Relationship Loader";
        Width = 960;
        Height = 680;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildLayout();

        _sourceConfigPathBox.Text = _config.SourceAppConfigPath;
        _schemaStudioBox.Text = _config.SchemaStudioConnection;
        _excedeBox.Text = _config.ExcedeSchemaConnection;
        _lookupsCheck.Checked = true;
        _childrenCheck.Checked = true;
        _colookupCheck.Checked = true;
        _ignoreSelfJoinsCheck.Checked = true;

        Shown += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_schemaStudioBox.Text))
            {
                await LoadDatabasesAsync(silentOnError: true);
            }
        };
    }

    private void BuildLayout()
    {
        // Log fills the remaining space; added FIRST so the docked inputs above claim the top.
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Both;
        _logBox.WordWrap = false;
        _logBox.Dock = DockStyle.Fill;
        _logBox.BackColor = Color.White;
        _logBox.Font = new Font("Consolas", 9f);
        Controls.Add(_logBox);

        var inputs = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Padding = new Padding(10),
        };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0: source app config -> read connection strings from it
        _sourceConfigPathBox.Dock = DockStyle.Fill;
        _readFromSourceButton.Text = "Read from Source App";
        _readFromSourceButton.AutoSize = true;
        _readFromSourceButton.Click += (_, _) => ReadFromSourceApp();
        AddRow(inputs, 0, "Source App Config:", _sourceConfigPathBox, _readFromSourceButton);

        // Row 1: database chooser
        _loadDbsButton.Text = "Load Databases";
        _loadDbsButton.AutoSize = true;
        _loadDbsButton.Click += async (_, _) => await LoadDatabasesAsync(silentOnError: false);
        _dbCombo.Dock = DockStyle.Fill;
        _dbCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        AddRow(inputs, 1, "Database:", _dbCombo, _loadDbsButton);

        // Row 2-3: connection strings (span the value + right column)
        _schemaStudioBox.Dock = DockStyle.Fill;
        AddRow(inputs, 2, "SchemaStudio Connection:", _schemaStudioBox, spanValue: true);
        _excedeBox.Dock = DockStyle.Fill;
        AddRow(inputs, 3, "Excede Schema Connection:", _excedeBox, spanValue: true);

        // Row 4: options
        _lookupsCheck.Text = "Include lookups (SchemaLookup)";
        _lookupsCheck.AutoSize = true;
        _childrenCheck.Text = "Include children (SchemaChild)";
        _childrenCheck.AutoSize = true;
        _colookupCheck.Text = "Include COLOOKUP lookups (via SQLLookupString)";
        _colookupCheck.AutoSize = true;
        _ignoreSelfJoinsCheck.Text = "Ignore self joins";
        _ignoreSelfJoinsCheck.AutoSize = true;
        var options = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
        options.Controls.Add(_lookupsCheck);
        options.Controls.Add(_childrenCheck);
        options.Controls.Add(_colookupCheck);
        options.Controls.Add(_ignoreSelfJoinsCheck);
        AddRow(inputs, 4, "", options, spanValue: true);

        // Row 5: actions
        _saveConfigButton.Text = "Save Config";
        _saveConfigButton.AutoSize = true;
        _saveConfigButton.Click += (_, _) => SaveConfig();
        _previewButton.Text = "Preview (Dry Run)";
        _previewButton.AutoSize = true;
        _previewButton.Click += async (_, _) => await RunAsync(dryRun: true);
        _loadButton.Text = "Load Relationships";
        _loadButton.AutoSize = true;
        _loadButton.Click += async (_, _) => await RunAsync(dryRun: false);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        actions.Controls.Add(_saveConfigButton);
        actions.Controls.Add(_previewButton);
        actions.Controls.Add(_loadButton);
        AddRow(inputs, 5, "", actions, spanValue: true);

        Controls.Add(inputs);
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control value, Control? third = null, bool spanValue = false)
    {
        table.RowCount = Math.Max(table.RowCount, row + 1);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var labelControl = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3),
        };
        table.Controls.Add(labelControl, 0, row);
        table.Controls.Add(value, 1, row);

        if (spanValue)
        {
            table.SetColumnSpan(value, 2);
        }
        else if (third is not null)
        {
            table.Controls.Add(third, 2, row);
        }
    }

    private void SaveConfig()
    {
        _config.SchemaStudioConnection = _schemaStudioBox.Text.Trim();
        _config.ExcedeSchemaConnection = _excedeBox.Text.Trim();
        _config.SourceAppConfigPath = _sourceConfigPathBox.Text.Trim();
        _config.Save();
        Log("Config saved to appsettings.json.");
    }

    // Starting-point implementation — pulls ConnectionStrings:DefaultConnection out of a source
    // app's appsettings.json. Replace or remove if you wire the source-connection read yourself.
    private void ReadFromSourceApp()
    {
        var path = _sourceConfigPathBox.Text.Trim();
        if (!File.Exists(path))
        {
            Log($"Source app config not found: {path}");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
                connectionStrings.TryGetProperty("DefaultConnection", out var defaultConnection) &&
                defaultConnection.ValueKind == JsonValueKind.String)
            {
                var connection = defaultConnection.GetString() ?? "";
                _schemaStudioBox.Text = connection;
                _excedeBox.Text = connection; // same server; discovery targets the chosen DB by name
                Log($"Read DefaultConnection from {Path.GetFileName(path)}.");
            }
            else
            {
                Log("ConnectionStrings:DefaultConnection not found in the source app config.");
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR reading source app config: {ex.Message}");
        }
    }

    private async Task LoadDatabasesAsync(bool silentOnError)
    {
        var connection = _schemaStudioBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(connection))
        {
            if (!silentOnError)
            {
                Log("SchemaStudio connection is empty — cannot load databases.");
            }

            return;
        }

        try
        {
            SetBusy(true);
            var databases = (await new DatabaseRepository(connection).GetAllAsync()).ToList();
            _dbCombo.DataSource = databases;
            _dbCombo.DisplayMember = nameof(DatabaseDefinition.DatabaseName);
            _dbCombo.ValueMember = nameof(DatabaseDefinition.DatabaseId);
            Log($"Loaded {databases.Count} database(s) from SchemaStudio.");
        }
        catch (Exception ex) when (silentOnError)
        {
            Log($"(Auto-load databases skipped: {ex.Message})");
        }
        catch (Exception ex)
        {
            Log($"ERROR loading databases: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunAsync(bool dryRun)
    {
        var schemaStudioConnection = _schemaStudioBox.Text.Trim();
        var excedeConnection = _excedeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(schemaStudioConnection) || string.IsNullOrWhiteSpace(excedeConnection))
        {
            Log("Both connection strings are required.");
            return;
        }

        if (_dbCombo.SelectedItem is not DatabaseDefinition selectedDatabase)
        {
            Log("Select a database first (Load Databases, then choose one).");
            return;
        }

        if (!_lookupsCheck.Checked && !_childrenCheck.Checked && !_colookupCheck.Checked)
        {
            Log("Nothing to do — enable lookups, children, or COLOOKUP lookups.");
            return;
        }

        try
        {
            SetBusy(true);
            Log(new string('-', 60));
            Log($"{(dryRun ? "PREVIEW" : "LOAD")} for database '{selectedDatabase.DatabaseName}' (Id {selectedDatabase.DatabaseId}).");

            var discovery = new RelationshipDiscovery(excedeConnection);
            var relationships = await discovery.DiscoverAsync(
                selectedDatabase.DatabaseId,
                selectedDatabase.DatabaseName,
                _lookupsCheck.Checked,
                _childrenCheck.Checked,
                _colookupCheck.Checked,
                selectedDatabase.SQLLookupString,
                _ignoreSelfJoinsCheck.Checked,
                Log);

            var lookupCount = relationships.Count(r => r.DiscoverySource == "SchemaLookup" && string.IsNullOrEmpty(r.FilterColumnName));
            var colookupCount = relationships.Count(r => r.DiscoverySource == "SchemaLookup" && !string.IsNullOrEmpty(r.FilterColumnName));
            var childCount = relationships.Count(r => r.DiscoverySource == "SchemaChild");
            Log($"  {lookupCount} FK lookup(s), {colookupCount} COLOOKUP lookup(s), {childCount} child row(s).");

            if (dryRun)
            {
                Log("DRY RUN — nothing will be written. Rows that WOULD be upserted:");
                foreach (var relationship in relationships)
                {
                    Log("  " + Describe(relationship));
                }

                Log($"Dry run complete: {relationships.Count} row(s) planned, 0 written. Manual/soft relationships are never touched.");
                return;
            }

            var confirm = MessageBox.Show(
                $"Upsert {relationships.Count} relationship row(s) into '{selectedDatabase.DatabaseName}'?" +
                    Environment.NewLine + Environment.NewLine +
                    "Hand-authored (Manual) relationships are NOT touched.",
                "Confirm Load Relationships",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                Log("Load cancelled by operator.");
                return;
            }

            var repository = new DatabaseRelationshipRepository(schemaStudioConnection);
            var upserted = 0;
            foreach (var relationship in relationships)
            {
                await repository.UpsertAsync(relationship);
                upserted++;
                if (upserted % 25 == 0)
                {
                    Log($"  upserted {upserted}/{relationships.Count}...");
                }
            }

            Log($"Done. Upserted {upserted} relationship row(s) into DatabaseRelationships / DatabaseRelationshipColumns.");
            Log("Manual (hand-authored) relationships were left untouched.");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string Describe(DatabaseRelationshipDefinition relationship)
    {
        var display = string.IsNullOrEmpty(relationship.DisplayColumnName)
            ? ""
            : $", display={relationship.DisplayColumnName}";
        return $"[{relationship.DiscoverySource}] " +
               $"{relationship.SourceSchemaName}.{relationship.SourceTableName} -> " +
               $"{relationship.TargetSchemaName}.{relationship.TargetTableName} " +
               $"({relationship.JoinType}{display}) : {relationship.JoinExpression}";
    }

    private void SetBusy(bool busy)
    {
        _loadDbsButton.Enabled = !busy;
        _saveConfigButton.Enabled = !busy;
        _previewButton.Enabled = !busy;
        _loadButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void Log(string message)
    {
        _logBox.AppendText(message + Environment.NewLine);
    }
}
