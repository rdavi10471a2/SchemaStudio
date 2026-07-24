using System.IO;
using System.Text.Json;

namespace RelationshipLoader;

// The two connection strings, persisted to appsettings.json next to the exe.
// SchemaStudioConnection  -> the metadata store (dbo.Databases, dbo.DatabaseRelationships[Columns]).
// ExcedeSchemaConnection  -> the source vendor DB whose foreign keys we read.
public sealed class AppConfig
{
    public string SchemaStudioConnection { get; set; } = "";
    public string ExcedeSchemaConnection { get; set; } = "";

    // Path to a source app's appsettings.json to read ConnectionStrings:DefaultConnection from.
    public string SourceAppConfigPath { get; set; } = @"C:\SchemaStudioWebViewer V 1.1 - Monitor\appsettings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            }
        }
        catch
        {
            // A malformed config should not stop the app; fall back to empty.
        }

        return new AppConfig();
    }

    public void Save() => File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, Options));
}
