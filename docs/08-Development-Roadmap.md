# 08 — Development Roadmap

## What is built (Chunk 1 — this delivery)

### Foundation

- ✅ Solution + Web project scaffold (ASP.NET Core 8 Blazor Server)
- ✅ Modular architecture with `IModule` framework
- ✅ Config-driven module toggles (`modules.json`)
- ✅ Licence-tier gating for future commercial distribution
- ✅ Microsoft Entra ID authentication with allowlist authorization
- ✅ SQL Server 2022 schema with EF Core 8
- ✅ Audit logging service (every mutation captured)
- ✅ Three-theme UI system (Light, Beige, Dark) with Nordic palette
- ✅ Responsive shell (sidebar navigation, top bar, dark-mode toggle)
- ✅ Inline SVG icon system (Lucide subset — no CDN dependency)
- ✅ Masked input component (PAN, Aadhaar, password) with eye-toggle
- ✅ Confirmation dialog component
- ✅ Git repository scaffolding (.gitignore, .editorconfig, README, LICENSE)
- ✅ Full deployment documentation

### Staff Profile module

- ✅ Comprehensive Staff entity — 35+ attributes covering:
  - Personal: photo, name, initial, display name, DOJ, designation
  - Contact: email, phone, alternate phone, WhatsApp, address, bus number
  - Statutory: PAN (masked), Aadhaar (masked), EPF UAN, EPF password
    (masked), ESIC number
  - Banking: account number, IFSC, passbook scan upload
  - Salary: Gross Pay (editable), all statutory calcs (auto-computed,
    read-only), Net Pay
- ✅ Designation options: Teaching, Non-Teaching, Admin, Driver, Cleaner, Aaya
- ✅ Auto-computed statutory calculations with **EPS ₹15,000 cap**:
  - Basic Wage = Net Pay × 50% (anchored to Net Pay, not Gross — see StatutorySalaryCalculator.cs for why)
  - Employee EPF = Basic × 12%
  - Employee ESIC = if Gross ≤ ₹21,000 then Gross × 0.75% else ₹0
  - Employer EPS = min(Basic, ₹15,000) × 8.33%
  - Employer EPF = (Basic × 12%) − Employer EPS
  - EDLI & Admin = Basic × 1%
  - Employer ESIC = if Gross ≤ ₹21,000 then Gross × 3.25% else ₹0
  - Net Pay = Gross − Employee EPF − Employee ESIC
- ✅ Directory view: searchable, filterable table with photo thumbnails,
  status badges, click-to-open profile
- ✅ Profile drawer (Entra ID-inspired slide-out): tabbed layout
  (Personal, Statutory, Banking, Salary), pencil icon to enter edit
  mode, real-time salary recalculation on Gross Pay edit
- ✅ Add Staff dialog with dual account-number confirmation
- ✅ Soft delete with 60-day retention: hidden from active views,
  reactivate button restores everything intact
- ✅ Bulk actions: multi-select checkbox + "Deactivate selected" button
- ✅ Automatic hard-delete after 60 days *(background sweep on startup +
  daily)*
- ✅ Staff photo upload (2 MB max, JPG/PNG/WebP)
- ✅ July 2026 seed script — 69 staff loaded from your uploaded files

## What's next (Chunk 2 — Attendance module)

### Core attendance

- Monthly attendance register (mark Present / Absent / Half-day / Late / Paid Leave / LWP)
- Total working days configurable per month
- Real-time daily-wage and LOP recalculation
- Paid leave balance tracker (Casual, Sick, Earned)

### Special attendance (planned)

- Late arrival tracker with grace period (10 min default)
- Optional "3 lates = 0.5 day deduction" rule
- Half-day recognition (< 5 hrs worked)
- Special class hours for teachers (post-6 PM, weekends)
- Special shift toggle for support staff (Driver, Cleaner, Aaya — weekend
  bus routes, extra cleaning shifts)
- Configurable per-hour incentive rate for teachers
- Configurable per-shift allowance for support staff
- Colour-coded badges on staff profile card (red = late, yellow =
  half-day, green = special contribution)

### UI

- Calendar-grid attendance marking (click day → cycle status)
- Monthly summary per staff
- WhatsApp-friendly export ("Ramya on leave 12 Aug, approved by Principal")

## Chunk 3 — Payroll module rebuild

### Statutory-compliant salary computation

- Uses Earned Gross (after LOP deductions) for all statutory calcs
- Monthly draft with clone-from-previous-month
- Statutory register outputs (Form 5A, EPF ECR-ready CSV)

### Bank upload files (unchanged output contract)

- IOB Teaching CSV — identical format to your current bank upload
- IOB Non-Teaching CSV — identical format
- Combined salary XLSX — teaching + non-teaching sheets with totals,
  words-in-Indian-numbering, EFT footer
- Combined PDF — printable/signable version of the XLSX

### Workflow features

- Draft / Published states
- Immutable published months (audit trail for any override)
- Preflight validation (duplicate accounts, zero amounts, IFSC checksum)
- Historical view of any past month
- Regenerate files from any past month

## Chunk 4 — Reporting, dashboards, polish

- Dashboard: this month total, YoY, last-three-months trend, alerts
- Academic year summaries (Jun-May)
- Compliance exports (EPF ECR, ESIC monthly return)
- Change reports (added/removed/changed staff month-over-month)
- Charts: payroll trend, headcount by designation
- Print-friendly staff directory export
- Search across all modules

## Beyond the current roadmap (candidates)

- Two-factor UI login (in addition to Entra MFA) for shared workstations
- Payslip PDF per staff, emailable via school M365 SMTP
- Bulk import from CSV/Excel (for onboarding a whole cohort at once)
- Multi-branch support (if you ever run more than one campus)
- Marathi / Tamil / Kannada UI translation for staff-facing views
- Mobile app (Blazor Hybrid or MAUI) for admin on the go
- Attendance marking via QR code / biometric device integration
- Statutory portal integrations (EPFO, ESIC) for auto-submission

## Non-goals (deliberately excluded)

- **Student information system.** This is staff-only. Student data
  belongs in a separate system with different privacy/compliance rules.
- **Accounts / bookkeeping.** Salary is one line item; a school's
  accounts also cover fees, expenses, assets. That's Tally / Zoho Books.
- **Timesheet approvals workflow.** For a 69-staff school, in-person
  approvals + WhatsApp are faster than any digital workflow. Revisit if
  you commercialize.

## Change management

- Each chunk is a coherent deliverable that leaves the app in a working
  state (tested, deployable).
- Between chunks, you can operate the app in production with only the
  Staff Profile module enabled. Later chunks add on without disruption.
- Every schema change is a versioned SQL script in `sql/`, so upgrades
  in production are: publish new build → run SQL scripts new since last
  version → restart app pool.
