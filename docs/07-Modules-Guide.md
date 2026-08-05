# 07 — Modules Guide

The application is built as a set of independent modules. This document
explains how to toggle them, and how they map to a commercial licensing
model if you decide to sell this to other schools later.

## The switchboard: `modules.json`

Lives at `src/ShanthiNikethan.EmployeeManagement.Web/modules.json`.
This is the ONLY file you need to edit to turn a module on or off.

```jsonc
{
  "Deployment": {
    "LicenseTier": "Full"
    // Base | Standard | Full
    // Determines the ceiling of modules this deployment can run.
  },
  "Modules": {
    "StaffProfile": {
      "Enabled": true,
      "LicenseTier": "Base"
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

A module runs if:
- `Enabled == true`, **AND**
- `Modules[X].LicenseTier <= Deployment.LicenseTier`

Restart the app after editing (IIS: `iisreset` or restart the app pool).

## Commercial tiers (proposed)

| Tier | Includes | Target customer |
|---|---|---|
| **Base** | Staff Profile | Small pre-schools, tutoring centres — just want a digital staff directory |
| **Standard** | Base + Attendance + Payroll | Any recognized school with staff of 20+ |
| **Full** | Standard + Reporting + Multi-branch + priority support | School chains, higher-secondary institutions |

When a customer buys tier X, ship them a `modules.json` where
`Deployment.LicenseTier` is set to X. The other modules' code is still
in the binary but never activates.

## Adding a new module (for developers / integration team)

Create the folder:

```
Modules/YourModule/
├── YourModuleModule.cs          — implements IModule
├── Data/                        — entities, EF config
├── Services/                    — DI-registered services
└── Components/                  — Razor UI (routes, forms, tables)
```

Implement `IModule`:

```csharp
namespace ShanthiNikethan.EmployeeManagement.Modules.YourModule;

public class YourModuleModule : IModule
{
    public string Name => "YourModule";
    public string DisplayName => "Your Module";
    public string Icon => "briefcase";     // Lucide icon name
    public string BasePath => "/your-module";
    public int NavigationOrder => 40;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IYourService, YourService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<YourEntity>(e =>
        {
            e.ToTable("YourEntity");
            e.HasKey(x => x.Id);
            // ... rest of entity config
        });
    }
}
```

Add its entry to `modules.json`. `ModuleRegistry` picks it up
automatically on next startup — no changes to `Program.cs`.

Add a Razor page in `Components/` with a `@page` directive matching your
`BasePath`, and it appears in the navigation sidebar automatically for
users whose deployment tier allows it.

## Guardrails when writing a module

- **Own your tables.** Prefix table names with your module name if
  collision is likely: `Attendance_DailyRecord`, not `DailyRecord`.
- **Depend on Core, not on other modules.** If Attendance needs staff
  info, it should look up via `IStaffLookupService` (defined in Core or
  StaffProfile's public interface), not `import` StaffProfile.Data types.
  For now this is a discipline; we can enforce with .NET assemblies if
  we split modules into projects later.
- **Add an audit trail.** Every mutation should call `IAuditService.LogAsync(...)`
  so nothing changes without a paper trail.
- **Design as if the module might be disabled.** Never assume Payroll is
  running when you're in Attendance. If you need cross-module data, use
  the `ModuleRegistry.IsEnabled("Payroll")` check.

## Disabling a module in production

Two scenarios:

**Temporary maintenance:** flip `Enabled: false`, restart the app.
Users see the sidebar item vanish. Existing data is untouched. Flip
back to `true` when done.

**Permanent removal for a customer:** set `Deployment.LicenseTier` to
a tier that excludes the module. Same visible effect, but the module
was never "on" from that deployment's perspective — cleaner.

## Discovering which modules are running

The bottom of every page shows a subtle "Modules: StaffProfile,
Attendance, Payroll" line for logged-in admin users. If a module is
supposed to be running but isn't, check the app logs for the startup
banner:

```
[Info] Module registry initialized. Enabled: StaffProfile, Attendance, Payroll. Disabled: Reporting.
```

If a module you expect to be there is in the "Disabled" list, check:
1. Its entry exists in `modules.json`
2. `Enabled: true` in that entry
3. Its `LicenseTier` is `<=` your `Deployment.LicenseTier`
