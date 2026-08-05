# 04 — Local Development

Get the app running on your local machine to test before deploying to IIS.

## Step 1 — Restore packages

Open PowerShell in the repo root:

```powershell
cd C:\ShanthiNikethan   # or wherever you unzipped

dotnet restore src/ShanthiNikethan.EmployeeManagement.sln
```

This downloads all NuGet dependencies (~50 MB, first time only). You'll
see a lot of output — success ends with "Restore complete".

## Step 2 — Build

```powershell
dotnet build src/ShanthiNikethan.EmployeeManagement.sln -c Debug
```

If this fails, the error output points to the file and line. Most common
issue: a missing NuGet package (rerun restore) or a syntax error from a
manual edit.

## Step 3 — Run

```powershell
dotnet run --project src/ShanthiNikethan.EmployeeManagement.Web
```

You'll see:

```
Now listening on: https://localhost:5001
Now listening on: http://localhost:5000
Application started. Press Ctrl+C to shut down.
```

Open <https://localhost:5001> in your browser. Your browser will complain
about the self-signed certificate — click **Advanced → Proceed to
localhost (unsafe)**. This is fine for local development.

You'll then be redirected to Microsoft login. Sign in with your school
account. First time, you may need to consent to the app's permissions.

## Step 4 — Making changes

Blazor Server has hot-reload for Razor components. With the app running,
edit any `.razor` file and save — the browser refreshes automatically.
For C# code changes, stop with Ctrl+C, rebuild, rerun.

## Common problems

**"Unable to connect to the database"**
- Is SQL Server (SQLEXPRESS) running? `Get-Service 'MSSQL$SQLEXPRESS'`
- Is your Windows user a `db_owner` on `ShanthiNikethanEmployeeManagement`? In SSMS,
  Databases → your DB → Security → Users → verify.

**"AADSTS50011: The reply URL specified in the request does not match"**
- The redirect URI in your Entra app doesn't include `https://localhost:5001/signin-oidc`.
  Add it in the Authentication blade of the app registration.

**"HTTP Error 500.30 - ANCM In-Process Start Failure"**
- This only happens under IIS. For local dev with `dotnet run`, you'll
  see the actual C# exception in the console.

**Blazor UI is stuck showing a spinner**
- Open browser dev tools (F12) → Console tab. Look for WebSocket errors.
  Blazor Server needs a WebSocket connection back to the server. If your
  browser or corporate proxy blocks WebSockets, use a different browser.
