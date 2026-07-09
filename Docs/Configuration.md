<!-- GENERATED from the AIMonitor solution index + AppConfig.cs on 2026-07-07. Regenerate, do not hand-patch. ASCII-only. -->

# Configuration Reference

> The answer surface for "which setting changes X, and where is it used." Solution map: [Architecture.md](Architecture.md).

## How config works

`appsettings.json` (+ `appsettings.Development.json`) is read **once** by
[AppConfig.Initialize](../AppConfig/AppConfig.cs#L20) (called from `Program.cs`) into a strongly-typed static
tree. Feature code **never** reads `IConfiguration` directly - it reads
**`AppConfig.Current.<Section>.<Property>`** (namespace `SchemaStudioWebViewer.Configuration`).

Two consequences:
- **Defaults live in code**, not in the file. A key absent from `appsettings.json` falls back to the code
  default in `AppConfig.Initialize` / the config class. Some code defaults differ from the shipped file (below).
- **To trace any setting:** config key -> its `ReadBool/ReadString` line in [AppConfig.cs](../AppConfig/AppConfig.cs) -> grep `AppConfig.Current.<Section>.<Property>` for usage sites.

`Logging` and `AllowedHosts` are **not** in `AppConfig` - they are consumed by the ASP.NET host directly.

## Sections and keys

Legend: **key** | code default | current appsettings.json | meaning.

### Auth (`SimpleAuthConfig`) - login gating (stub auth)

| Key | Code default | appsettings.json | Meaning |
|---|---|---|---|
| `Auth:Enabled` | false | **true** | Master switch for the simple-auth gate. |
| `Auth:InDebug` | false | false | Debug bypass/behavior for auth. |
| `Auth:RequireLoginForHome` | false | **true** | Gate the Home page behind login. Used: [MainLayout.razor:341](../Components/Layout/MainLayout.razor#L341), [Home.razor:474](../Components/Pages/Home.razor#L474). |
| `Auth:RequireLoginForAdmin` | true | true | Gate admin surfaces behind login. |
| `Auth:LoginPath` | /login | /login | Redirect target for login. |
| `Auth:ShowAuthButton` | true | true | **The login button.** Used: [Home.razor:118](../Components/Pages/Home.razor#L118) `<RadzenButton Visible="@AppConfig.Current.Auth.ShowAuthButton">`; gate logic [Home.razor:515](../Components/Pages/Home.razor#L515). **This is the one to toggle to show/hide the login button.** |

### Kiosk (`KioskConfig`) - unauthenticated demo display

| Key | Code default | appsettings.json | Meaning |
|---|---|---|---|
| `Kiosk:Enabled` | true | true | Enable kiosk mode. |
| `Kiosk:Route` | /kiosk | /kiosk | Kiosk route. |
| `Kiosk:HideNavigationChrome` | true | true | Hide nav chrome in kiosk. |
| `Kiosk:RequireLogin` | false | false | Whether kiosk requires login. |
| `Kiosk:ShowLoginLink` | false | false | A **nav login link** (distinct from `Auth:ShowAuthButton`). No `AppConfig.Current.Kiosk.ShowLoginLink` usage found by index/grep on 2026-07-07 - verify it is wired before relying on it. |

### Mcp (`McpConfig`) - embedded MCP server the app hosts

| Key | Code default | appsettings.json | Meaning |
|---|---|---|---|
| `Mcp:Enabled` | false | **true** | Mount the app's own MCP endpoint. |
| `Mcp:BaseRoute` | /mcp | /mcp | Base route. |
| `Mcp:UseSse` | true | **false** | SSE transport toggle. `EffectiveRoute` = `BaseRoute + "/sse"` when true, else `BaseRoute` ([AppConfig.cs:79](../AppConfig/AppConfig.cs#L79)); currently effective route is `/mcp`. |

### ConnectionStrings (`DBConnectionString`)

| Key | Code default | Meaning |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | "" | SQL Server connection for all `SchemaStudio.Data` repositories. |

## Common tasks

- **Show/hide the login button:** set `Auth:ShowAuthButton` (true = shown). Not `Kiosk:ShowLoginLink`.
- **Open the app without login for a demo:** `Auth:RequireLoginForHome = false` (and check `Kiosk:Enabled`).
- **Turn the embedded MCP server off:** `Mcp:Enabled = false`.

## Refresh

Regenerate: parse [AppConfig.cs](../AppConfig/AppConfig.cs) for sections/keys/defaults; read `appsettings.json`
for current values; for each `AppConfig.Current.X.Y`, `find_indexed_references` on the property (grep the
markup for `.razor` bindings the C# index misses). Flag keys with no found usage rather than omitting them.
