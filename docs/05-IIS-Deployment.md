# 05 — IIS Deployment

Once the app runs locally with `dotnet run`, deploy it to IIS so users
can reach it over the internet.

## Step 1 — Publish the app

From an admin PowerShell in the repo root:

```powershell
dotnet publish src/ShanthiNikethan.EmployeeManagement.Web -c Release -o C:\inetpub\ShanthiNikethan
```

This creates a self-contained folder at `C:\inetpub\ShanthiNikethan`
with the compiled app, dependencies, and static files.

## Step 2 — Edit production appsettings

Copy `appsettings.json` to `appsettings.Production.json` in the
publish folder, and edit:

- `ConnectionStrings:Default` — leave as-is unless SQL is on a different host
- `AzureAd:TenantId`, `AzureAd:ClientId` — fill in from your Entra app
- `Authorization:AllowedUserObjectIds` — your + finance officer Object IDs
- `Kestrel:Endpoints` — remove if present (IIS handles bindings)

## Step 3 — Create the IIS site

Open **IIS Manager**:

1. Right-click **Sites → Add Website**
2. **Site name:** `ShanthiNikethan`
3. **Physical path:** `C:\inetpub\ShanthiNikethan`
4. **Binding:** HTTP, port 80, host name = your DNS name
   (e.g. `staff.shanthinikethan.edu.in`) or leave blank for IP-only
5. Click **OK**

## Step 4 — App pool configuration

1. Left tree → **Application Pools → ShanthiNikethan**
2. **Advanced Settings...**:
   - **.NET CLR Version:** `No Managed Code` (ASP.NET Core doesn't use the .NET Framework CLR)
   - **Identity:** `ApplicationPoolIdentity` (default is fine)
   - **Idle Time-out:** `0` (never idle out — the app is small enough that
     always-on is cheap)
   - **Start Mode:** `AlwaysRunning` (needs Application Initialization feature enabled)

## Step 5 — Grant SQL access to the app pool

By this point you've created the `ShanthiNikethan` app pool in Step 4, so
Windows now has a virtual account called `IIS APPPOOL\ShanthiNikethan`.
Grant it access to the database. In SSMS, connected as your admin user:

```sql
USE master;
CREATE LOGIN [IIS APPPOOL\ShanthiNikethan] FROM WINDOWS;

USE ShanthiNikethanEmployeeManagement;
CREATE USER [IIS APPPOOL\ShanthiNikethan] FROM LOGIN [IIS APPPOOL\ShanthiNikethan];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\ShanthiNikethan];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\ShanthiNikethan];
GRANT EXECUTE ON SCHEMA::dbo TO [IIS APPPOOL\ShanthiNikethan];
```

Only copy the lines between the fences above — not the ` ```sql ` marker
line itself, or SSMS will try to run `sql` as a command and fail with
`Could not find stored procedure 'sql'`.

If you get `Windows NT user or group 'IIS APPPOOL\ShanthiNikethan' not
found`, the app pool name doesn't match exactly, or Step 4 above hasn't
been completed yet — go back and confirm the app pool is named exactly
`ShanthiNikethan` (case doesn't matter, spelling does).

## Step 6 — First HTTP test

Browse to `http://your-domain-or-ip/`. Expected:
- Redirect to Microsoft login
- After sign-in, land on the dashboard

If you see `HTTP Error 500.30`: Hosting Bundle not installed. Go back to
[`01-Prerequisites.md`](01-Prerequisites.md#step-1--install-aspnet-core-hosting-bundle-80-critical).

If you see `HTTP Error 500.19 - Cannot read configuration file`:
`appsettings.json` is missing or malformed. Compare it against the source.

## Step 7 — HTTPS via Let's Encrypt (win-acme)

1. Extract win-acme somewhere (e.g. `C:\tools\win-acme\`)
2. Open admin PowerShell in that folder
3. Run `.\wacs.exe`
4. Choose:
   - `N` — Create certificate (default settings)
   - Select `ShanthiNikethan` from the site list
   - Accept defaults for the rest
5. Enter your email for renewal notices; accept Let's Encrypt ToS

win-acme installs the cert, binds it to port 443 on your site, and
registers a scheduled task to auto-renew every 60 days.

## Step 8 — Force HTTPS via URL Rewrite

With URL Rewrite Module installed (see prereqs):

1. IIS Manager → your site → **URL Rewrite** icon
2. **Add Rule → Blank rule**
3. **Match URL:** pattern `(.*)`
4. **Conditions:** add condition `{HTTPS}` matches pattern `off`
5. **Action:** Redirect to `https://{HTTP_HOST}/{R:1}`, type `Permanent (301)`
6. **Apply**

Now any HTTP request auto-redirects to HTTPS.

## Step 9 — Firewall

Windows Firewall → Inbound Rules → allow ports **80** (needed for
Let's Encrypt renewal HTTP-01 challenge) and **443**. Block everything
else you don't need.

## Applying updates

When you have a new build to deploy:

```powershell
# 1. Backup current deployment
Compress-Archive C:\inetpub\ShanthiNikethan D:\backups\deploy-$(Get-Date -f yyyy-MM-dd-HHmm).zip

# 2. Stop the app pool (releases file locks)
Stop-WebAppPool -Name ShanthiNikethan

# 3. Republish over the existing folder
dotnet publish src/ShanthiNikethan.EmployeeManagement.Web -c Release -o C:\inetpub\ShanthiNikethan

# 4. Start the app pool
Start-WebAppPool -Name ShanthiNikethan
```

## Troubleshooting

**Logs:** IIS ASP.NET Core Module logs go to Windows Event Viewer →
Application. Enable stdout logging by editing `web.config` in the
publish folder:

```xml
<aspNetCore ... stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" />
```

Then check `C:\inetpub\ShanthiNikethan\logs\`. Remember to disable it in
production once you've solved the problem — it's verbose.
