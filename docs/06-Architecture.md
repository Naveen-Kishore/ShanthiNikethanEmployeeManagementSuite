# 06 — Architecture

## Design principles

1. **Modular by construction.** Each feature area lives in its own
   `Modules/<Name>/` folder with its own entities, services, and UI. A
   module can be turned off in configuration without touching code.
2. **Configuration-driven.** A single `modules.json` file controls which
   modules run and at what licence tier. This is the seed of the
   commercial gating you asked about.
3. **Shared kernel.** `Core/` holds cross-cutting concerns (authentication,
   database context, audit, theming). Modules depend on Core, not on each
   other.
4. **Blazor Server, not Web API.** The UI is written in C# using Razor
   components. This means one deployment, one codebase, real-time updates,
   and full type safety end-to-end. No separate frontend build.
5. **Nothing childish, nothing overengineered.** Flat modern icons (Lucide),
   Nordic-inspired palette, three theme modes (light/beige/dark), no
   corporate stock photos, no external CDN dependencies.

## Physical layout

```
ShanthiNikethan.EmployeeManagement.Web/
│
├── Program.cs                     — startup, DI wiring, module registration
├── appsettings.json               — configuration (connection strings, Entra)
├── modules.json                   — MODULE ON/OFF SWITCHES
│
├── Core/                          — shared kernel, always loaded
│   ├── Data/AppDbContext.cs       — EF Core context (all modules add their entities here)
│   ├── Modules/                   — the module framework
│   │   ├── IModule.cs             — interface every module implements
│   │   ├── ModuleConfiguration.cs — POCO for modules.json
│   │   └── ModuleRegistry.cs      — discovers, filters, registers modules
│   ├── Authorization/             — Entra allowlist policy
│   └── Services/                  — audit, current user, theme
│
├── Modules/                       — one folder per feature area
│   ├── StaffProfile/
│   │   ├── StaffProfileModule.cs  — implements IModule; declares routes, nav, services
│   │   ├── Data/                  — entities, EF configuration
│   │   ├── Services/              — business logic
│   │   └── Components/            — Razor UI
│   ├── Attendance/     (planned)
│   ├── Payroll/        (planned)
│   └── Reporting/      (planned)
│
├── Shared/
│   ├── Components/
│   │   ├── App.razor
│   │   ├── Routes.razor
│   │   ├── Layout/                — MainLayout, sidebar, top bar
│   │   ├── ThemeToggle.razor
│   │   ├── MaskedField.razor      — PAN/Aadhaar/password with eye-toggle
│   │   ├── ConfirmDialog.razor
│   │   └── Icon.razor             — inline SVG icon component (Lucide set)
│   └── Services/
│       ├── CurrentUser.cs         — extracts Entra identity
│       ├── AuditService.cs        — records every mutation
│       └── ThemeService.cs        — persists theme choice
│
└── wwwroot/
    ├── css/app.css                — Nordic palette, 3 theme modes
    ├── js/theme.js                — theme switching + persistence
    ├── icons/                     — SVG icon library (Lucide subset)
    └── uploads/                   — staff photos, passbook scans (NOT in git)
```

## How a module registers itself

Every module implements `IModule`:

```csharp
public interface IModule
{
    string Name { get; }             // "StaffProfile"
    string DisplayName { get; }      // "Staff Profile"
    string Icon { get; }             // Lucide icon name, e.g. "users"
    string BasePath { get; }         // "/staff"
    int NavigationOrder { get; }     // sort order in the sidebar
    void RegisterServices(IServiceCollection services);
    void ConfigureDbContext(ModelBuilder modelBuilder);
}
```

`ModuleRegistry` in `Core/Modules/`:

1. Reads `modules.json`
2. Finds all `IModule` implementations in the assembly
3. Filters to those whose config entry has `Enabled: true` **and** whose
   `LicenseTier` is included in the current deployment's licence
4. Calls `RegisterServices` on each enabled module
5. Exposes the enabled list to the navigation sidebar and route auth

## modules.json — the switchboard

```jsonc
{
  "Deployment": {
    "LicenseTier": "Full"        // "Base" | "Standard" | "Full"
  },
  "Modules": {
    "StaffProfile": {
      "Enabled": true,
      "LicenseTier": "Base"       // included in every tier
    },
    "Attendance": {
      "Enabled": true,
      "LicenseTier": "Standard"
    },
    "Payroll": {
      "Enabled": true,
      "LicenseTier": "Standard"
    },
    "Reporting": {
      "Enabled": false,
      "LicenseTier": "Full"
    }
  }
}
```

Tier ordering: `Base < Standard < Full`. A module runs only if:
- `Enabled: true`, AND
- `Modules[X].LicenseTier <= Deployment.LicenseTier`

To commercialize:
- Distribute the same binary to every customer
- Ship each with a `modules.json` matching what they've paid for
- (Later, sign the config file so customers can't upgrade themselves)

## Data flow

```
Browser (Blazor Server, WebSocket)
   ↕
Kestrel behind IIS reverse proxy
   ↕
Blazor Component → Service (per module) → DbContext → SQL Server
                        ↓
                    AuditService → AuditLog table
```

Blazor Server means UI state lives on the server, wire-synced to the
browser over a WebSocket. No REST API, no JSON serialization, no
duplicated types. The client is deliberately thin.

## Authentication and authorization

- **Authentication**: Microsoft Entra ID (OpenID Connect), user's M365
  account. MFA enforced by the tenant's Security Defaults (free with A1).
- **Authorization**: two layers
  1. Tenant restriction (single-tenant Entra app registration means only
     your school's M365 users can attempt to sign in at all)
  2. `AllowedUserObjectIds` allowlist in `appsettings.json` — only listed
     Entra user Object IDs pass the fallback policy
- Every route requires authentication. There is no public surface.

## Audit

Every mutation (create, update, soft-delete, reactivate) records:
- Who (Entra display name + Object ID)
- When (UTC)
- What entity + entity ID
- Field name (if applicable)
- Old value, new value
- Free-text context ("July 2026 payroll", "reactivation after 30 days", etc.)

Audit records are append-only and cannot be edited from the UI.

## Theming

Three themes: **Light**, **Beige** (warm off-white, low eye strain in
tropical daylight), **Dark**.

All colours are CSS custom properties defined per theme in `app.css`.
Switching theme is a `data-theme` attribute swap on `<html>` — no
reload, no flash. Choice is persisted to `localStorage` so it survives
sign-out.

Palette anchor points (Nord-inspired, softened for warmth):
- Primary accent: muted deep blue `#4C6E9C` (Nord "frost")
- Success: forest green `#68865E`
- Warning: warm amber `#B08850`
- Danger: subdued brick `#A05555`

No pure black or pure white anywhere — text at rest is `#1E2530` on light,
`#E4E7ED` on dark.

## What this architecture makes easy

- Adding the Attendance module: create `Modules/Attendance/`, implement
  `IModule`, add its section to `modules.json`. No changes to any
  existing file.
- Selling a customer the "Staff Profile only" tier: ship them a
  `modules.json` with `Deployment.LicenseTier: "Base"`. Payroll code is
  still in the binary but never registered, never routed, invisible.
- Rolling back a bad module: flip `Enabled: false`, restart the app.
  Users see the module vanish from the sidebar; no data lost.

## What this architecture makes harder (and why we accept it)

- **Not microservices.** Everything runs in one process. If Payroll
  crashes, it can take the whole app down. For a 69-staff school, the
  simplicity trade is right. If you ever scale to 500+ schools per
  tenant, revisit.
- **Direct EF references between modules.** In principle a module could
  query another module's tables. Discipline (and code review) keeps this
  clean. For a hard boundary, we'd split into separate projects — too
  heavy for now.
