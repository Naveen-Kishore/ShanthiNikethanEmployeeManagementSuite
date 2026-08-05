# 01 — Prerequisites

This is a **one-time** setup on your Windows VM. If you've already done
some of these (like SQL Express and IIS), just verify and skip.

## Prerequisites checklist

| Item | Status | Where to get it |
|---|---|---|
| Windows 11 / Windows Server | ✅ Already have | — |
| .NET SDK 8.0.x | ✅ 8.0.423 installed | — |
| **ASP.NET Core Hosting Bundle 8.0** | ⚠️ **Missing — install** | <https://dotnet.microsoft.com/download/dotnet/8.0> → "Hosting Bundle" |
| SQL Server 2022 Express | ✅ Installed | — |
| SQL Server Management Studio (SSMS) | ✅ 18.11.1 installed | — |
| Microsoft ODBC Driver 17+ / OLE DB Driver 18+ | ✅ Installed | — |
| IIS with required features | ⚠️ Mostly enabled — see below | Turn Windows features on/off |
| **IIS URL Rewrite Module 2.1** | ⚠️ **Missing — install** | <https://www.iis.net/downloads/microsoft/url-rewrite> |
| win-acme (for Let's Encrypt) | ✅ 2.2.9 downloaded | — |
| Git for Windows | ⚠️ Recommended if you plan to use GitHub | <https://git-scm.com/download/win> |

## Step 1 — Install ASP.NET Core Hosting Bundle 8.0 (CRITICAL)

The .NET SDK you already have compiles code. The **Hosting Bundle** is
what actually lets IIS serve ASP.NET Core applications. Without it, the
first request to your app will return HTTP 500.30 with a cryptic error.

1. Go to <https://dotnet.microsoft.com/download/dotnet/8.0>
2. Scroll to **ASP.NET Core Runtime 8.0.x**
3. Under "Run apps - Runtime", click **Hosting Bundle** (Windows)
4. Run the installer → Next → Install → Restart when prompted
5. Verify in an admin PowerShell:
   ```powershell
   dotnet --list-runtimes
   ```
   You should see `Microsoft.AspNetCore.App 8.0.x` in the output.
6. Restart IIS to pick up the module:
   ```powershell
   iisreset
   ```

## Step 2 — Install IIS URL Rewrite Module (CRITICAL for HTTPS redirect)

1. Download from <https://www.iis.net/downloads/microsoft/url-rewrite>
2. Choose your language, download `rewrite_amd64_en-US.msi`
3. Run the installer → Accept → Install
4. No restart needed. Verify in IIS Manager: open any site → you should
   see a **URL Rewrite** icon in the middle pane.

## Step 3 — Enable additional IIS features

Open **Turn Windows features on or off** and enable these under
**Internet Information Services**:

Under **World Wide Web Services → Application Development Features**:
- ✅ Application Initialization *(prevents cold-start after idle timeout)*
- ✅ ASP.NET 4.8 *(you already have this)*
- ✅ ISAPI Extensions
- ✅ ISAPI Filters

Under **World Wide Web Services → Common HTTP Features**:
- ✅ HTTP Redirection *(you already have this)*

Under **World Wide Web Services → Security**:
- ✅ Request Filtering *(you already have this)*
- ✅ Windows Authentication *(you already have this — useful for SSMS-style local admin)*

Click **OK** and let Windows install.

## Step 4 — Install Git for Windows (recommended)

If you plan to use the GitHub workflow described in
[`09-Git-Repository-Setup.md`](09-Git-Repository-Setup.md):

1. Download from <https://git-scm.com/download/win>
2. Run installer, accept defaults except:
   - **Default editor:** VS Code or Notepad (not Vim, unless you know Vim)
   - **PATH environment:** "Git from the command line and also from 3rd-party software"
   - **Line endings:** "Checkout Windows-style, commit Unix-style" (the default)
3. Verify:
   ```powershell
   git --version
   # git version 2.4x.x.windows.1
   ```

## Step 5 — Verify SQL Server is reachable

1. Open SSMS
2. Server name: `.\SQLEXPRESS` (or `localhost\SQLEXPRESS`)
3. Authentication: Windows Authentication
4. Click **Connect**

If this fails, open SQL Server Configuration Manager and make sure the
`SQL Server (SQLEXPRESS)` service is running and TCP/IP is enabled.

## Verification checklist before moving on

Run this in an admin PowerShell:

```powershell
# Should list "Microsoft.AspNetCore.App 8.0.x"
dotnet --list-runtimes

# Should list the SDK
dotnet --list-sdks

# Should return "Running"
Get-Service W3SVC | Select-Object Status

# Should return "Running"
Get-Service 'MSSQL$SQLEXPRESS' | Select-Object Status

# Should print a version like "2.4x.x.windows.1"
git --version
```

If all four commands succeed, you're ready for
[`02-Database-Setup.md`](02-Database-Setup.md).
