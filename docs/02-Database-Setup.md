# 02 — Database Setup

## Step 1 — Connect to SQL Express with SSMS

Open **SQL Server Management Studio (SSMS)**, connect with:

- **Server name:** `.\SQLEXPRESS` (or `localhost\SQLEXPRESS`)
- **Authentication:** Windows Authentication
- **Encrypt connection:** Optional (unchecked is fine for localhost)

Click **Connect**.

## Step 2 — Run the schema scripts in order

Open each file in SSMS (File → Open → File), and run each with **F5**.
Do them in this order — later scripts depend on earlier ones:

1. **`sql/01-Core-Schema.sql`**
   Creates the database `ShanthiNikethanEmployeeManagement`, plus the
   cross-cutting tables: `AuditLog`, `ModuleState`, and helper views.

2. **`sql/02-StaffProfile-Schema.sql`**
   Creates the `Staff` table with all attributes (personal, statutory,
   banking, salary, meta), plus indexes and constraints.

3. **`sql/99-Seed-July-2026.sql`** *(optional)*
   Loads your 69 staff members from the July 2026 payroll files with
   minimum viable data (name, account number, designation, current
   monthly amount treated as Net Pay). You'll fill in the rest via the UI.

## Step 3 — Verify

Still in SSMS, run:

```sql
USE ShanthiNikethanEmployeeManagement;

SELECT Designation, COUNT(*) AS TotalStaff
FROM Staff
WHERE SoftDeletedAtUtc IS NULL
GROUP BY Designation;
```

(The soft-delete column is called `SoftDeletedAtUtc`, not `DeletedAt` — matches
the `Staff` table definition in `sql/02-StaffProfile-Schema.sql`.)

You should see teaching + non-teaching totalling 69 (47 + 22) if you ran
the seed script. If you skipped it, the result is empty — you'll add
staff via the UI after first login.

## Step 4 — Grant IIS access to the database (do this later, not now)

⚠️ **Skip this step for now.** The SQL below references a Windows account
called `IIS APPPOOL\ShanthiNikethan`, which doesn't exist yet — Windows only
creates it the moment you create an IIS Application Pool named
`ShanthiNikethan`, which happens in
[`05-IIS-Deployment.md`](05-IIS-Deployment.md), Step 4. If you run it now,
you'll get `Windows NT user or group 'IIS APPPOOL\ShanthiNikethan' not
found`.

Come back to this once you've completed Step 4 of
[`05-IIS-Deployment.md`](05-IIS-Deployment.md#step-4--app-pool-configuration)
— the exact SQL to run is there, at
[Step 5](05-IIS-Deployment.md#step-5--grant-sql-access-to-the-app-pool).

For now, local development (`dotnet run`) works fine without this — your
Windows user already has access via Windows Authentication.

## Where the connection string lives

`src/ShanthiNikethan.EmployeeManagement.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "Default": "Server=.\\SQLEXPRESS;Database=ShanthiNikethanEmployeeManagement;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

If your SQL instance name is different (e.g. `.\MSSQLSERVER01`),
edit this string accordingly.

## Backing up the database

Add this scheduled task on your VM (Task Scheduler → Create Basic Task,
daily at 3 AM):

```powershell
$dt = Get-Date -Format 'yyyy-MM-dd'
$out = "D:\backups\ShanthiNikethan-$dt.bak"
Invoke-Sqlcmd -ServerInstance ".\SQLEXPRESS" -Query "BACKUP DATABASE ShanthiNikethanEmployeeManagement TO DISK = '$out' WITH COMPRESSION, INIT"
# Delete backups older than 30 days
Get-ChildItem D:\backups\ShanthiNikethan-*.bak | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item
```

The `.bak` files are small (< 20 MB for 69 staff + 5 years of history).
Copy them to Azure Blob or OneDrive weekly if you want offsite backup.
