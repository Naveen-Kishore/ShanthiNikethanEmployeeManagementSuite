# Shanthi Nikethan Employee Management

A modular ASP.NET Core 8 web application for school staff administration.
Built for **Shanthi Nikethan Matric Higher Secondary School, Arumbavur**,
with a modular architecture designed so individual modules can be enabled,
disabled, or licensed separately.

## What it does

- **Staff Profile** — comprehensive employee records with photo, contact,
  statutory IDs (PAN, Aadhaar, EPF, ESIC), banking, and salary structure
- **Attendance & Leave** *(planned)* — daily attendance, paid leave,
  loss-of-pay, late arrivals, half-days, special/weekend shifts for support staff
- **Payroll** *(planned)* — statutory-compliant salary computation (EPF, ESIC,
  EPS with ₹15,000 cap), IOB bulk-upload CSV, printable XLSX/PDF wage lists
- **Reporting** *(planned)* — monthly registers, compliance exports,
  academic-year summaries

## Architecture at a glance

```
Web application (ASP.NET Core 8 + Blazor Server)
├── Core          — shared services (auth, data, module registry, theme)
├── Modules       — each module is self-contained and independently toggleable
│   ├── StaffProfile
│   ├── Attendance  (planned)
│   ├── Payroll     (planned)
│   └── Reporting   (planned)
└── Shared        — layout, common UI components
```

Every module is registered via a single `modules.json` file. To turn a
module off, flip its `Enabled` flag — no code recompile needed. To make
one a paid add-on, set its `LicenseTier` to `Premium`. See
[`docs/07-Modules-Guide.md`](docs/07-Modules-Guide.md).

## Getting started

Follow the docs in order:

1. [Prerequisites](docs/01-Prerequisites.md) — one-time software install
2. [Database Setup](docs/02-Database-Setup.md) — create SQL Server database
3. [Entra Authentication](docs/03-Entra-Authentication.md) — register the app in Microsoft Entra
4. [Local Development](docs/04-Local-Development.md) — run it on your machine
5. [IIS Deployment](docs/05-IIS-Deployment.md) — publish to your Windows Server
6. [Architecture](docs/06-Architecture.md) — how the pieces fit together
7. [Modules Guide](docs/07-Modules-Guide.md) — enable/disable, license tiers
8. [Development Roadmap](docs/08-Development-Roadmap.md) — what's built, what's next
9. [Git Repository Setup](docs/09-Git-Repository-Setup.md) — push to GitHub

## Technology stack

| Layer | Choice | Why |
|---|---|---|
| Runtime | .NET 8 | LTS until Nov 2026, matches your existing website |
| UI | Blazor Server | Real-time interactive UI, single deployment, C# throughout |
| Data | SQL Server 2022 Express + EF Core 8 | Free, on-prem, matches your existing setup |
| Auth | Microsoft Entra ID (M365 A1) | You already pay for it; MFA free via Security Defaults |
| PDF | QuestPDF Community | Free for revenue < ₹8 Cr, MIT-style licence |
| Spreadsheet | ClosedXML | MIT licence, no paid dependency |
| Icons | Lucide (inline SVG) | Flat modern icons, no CDN dependency, no runtime bloat |
| Hosting | IIS on Windows Server + Let's Encrypt via win-acme | Zero cost, uses infrastructure you already have |

## Licence

Copyright © Shanthi Nikethan Matric Higher Secondary School. All rights reserved.
See [`LICENSE`](LICENSE).

## Contact & support

Repository maintainer: (to be updated)
For deployment support, see [`docs/`](docs/).
