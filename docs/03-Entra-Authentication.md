# 03 — Entra ID Authentication

One-time setup, ~5 minutes. Registers the app in your M365 tenant so
users can sign in with their school account (with MFA enforced by
Security Defaults, free with M365 A1).

## Step 1 — Register the app

1. Go to <https://entra.microsoft.com/>
2. Sign in with your school M365 admin account
3. Left menu → **Applications → App registrations → + New registration**
4. Fill in:
   - **Name:** `Shanthi Nikethan Employee Management`
   - **Supported account types:** Accounts in this organizational directory only (Single tenant)
   - **Redirect URI:** Web → `https://your-domain-or-ip/signin-oidc`
     (temporarily `https://localhost:5001/signin-oidc` for local dev)
5. Click **Register**

## Step 2 — Copy IDs

On the Overview page, copy:
- **Application (client) ID** → goes into `AzureAd:ClientId` in appsettings.json
- **Directory (tenant) ID** → goes into `AzureAd:TenantId`

## Step 3 — Configure auth settings

1. Left menu → **Authentication**
2. Under **Implicit grant and hybrid flows**, tick **ID tokens**
3. **Front-channel logout URL:** `https://your-domain-or-ip/signout-callback-oidc`
4. Save

## Step 4 — Add users to allowlist

Get your own Entra Object ID:
1. Entra home → **Users → All users → (your name)**
2. Copy **Object ID** (looks like a GUID)

Paste it into `Authorization:AllowedUserObjectIds` in `appsettings.json`.
Add other allowed users (e.g. finance officer) the same way. Anyone not
in this list is signed in by Entra but blocked by the app.

## Step 5 — Verify Security Defaults are on (MFA)

Entra home → **Identity → Overview → Properties**. Ensure **Security
defaults** is **Enabled**. This enforces MFA on all sign-ins at zero cost.

## Local development

For local `dotnet run`, add `https://localhost:5001/signin-oidc` to the
Entra app's redirect URIs. You can register multiple redirect URIs
(local + production) on the same app.
