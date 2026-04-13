namespace SchemaStudioWebViewer.Configuration
{
    public class AppConfig
    {
        public static AppConfig Current { get; private set; } = new AppConfig();

        public DBConnectionString ConnectionStrings { get; set; } = new DBConnectionString();

        public McpConfig Mcp { get; set; } = new McpConfig();

        public static void Initialize(IConfiguration configuration)
        {
            Current = configuration.Get<AppConfig>();

            if (Current == null)
            {
                throw new Exception("Failed to load AppConfig from appsettings.json.");
            }
        }
    }

    public class DBConnectionString
    {
        public string DefaultConnection { get; set; } = "";
    }

    public class McpConfig
    {
        public bool Enabled { get; set; } = false;

        public string BaseRoute { get; set; } = "/mcp";

        public bool UseSse { get; set; } = true;

        public string EffectiveRoute
        {
            get
            {
                if (UseSse)
                {
                    return $"{BaseRoute.TrimEnd('/')}/sse";
                }

                return BaseRoute;
            }
        }
    }
}