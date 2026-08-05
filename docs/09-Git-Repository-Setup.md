# 09 — Git Repository Setup

Steps to move this codebase from a folder on your VM into a proper GitHub
repository, and how to use Claude Code for ongoing edits directly against
the repo.

## Why this matters

- **Backup:** if your VM disk dies, your code is safe in GitHub
- **History:** every change is versioned, you can roll back mistakes
- **Collaboration:** integration team can review and contribute
- **Claude Code integration:** you can hand Claude Code a repo URL and it
  will make changes, commit, and push — no more copy-pasting files from chat

## Step 1 — Create a GitHub account and repository

1. Sign up at <https://github.com/> if you don't have an account.
   Consider using your school domain email so ownership is clear.
2. On GitHub, click **+ → New repository**
3. Fill in:
   - **Repository name:** `ShanthiNikethan.EmployeeManagement`
   - **Description:** School staff administration system
   - **Visibility:** **Private** (important — this contains staff PII)
   - ❌ Do NOT initialize with README, .gitignore, or license — we already
     have them locally
4. Click **Create repository**
5. On the next page, GitHub shows a section titled **"…or push an existing
   repository from the command line"**. Copy the URL shown; it looks like:
   `https://github.com/your-username/ShanthiNikethan.EmployeeManagement.git`

## Step 2 — Initialize git in your local folder

Open PowerShell in the folder containing `README.md`, `docs/`, `src/` etc:

```powershell
cd C:\ShanthiNikethan   # or wherever you unzipped the delivery

git init
git branch -M main
git config user.name "Your Name"
git config user.email "you@shanthinikethan.edu.in"

git add .
git status              # review what's about to be committed
git commit -m "Initial commit: foundation + Staff Profile module"
```

## Step 3 — Push to GitHub

```powershell
git remote add origin https://github.com/your-username/ShanthiNikethan.EmployeeManagement.git
git push -u origin main
```

The first push will prompt for GitHub credentials. Use a **Personal Access
Token** (PAT), not your password:

1. GitHub → Settings → Developer settings → Personal access tokens →
   Tokens (classic) → **Generate new token (classic)**
2. Note: `SNMHSS VM push access`
3. Expiration: 90 days (renew when it expires)
4. Scopes: **`repo`** (full control of private repos)
5. Generate, copy the token immediately (you'll never see it again)
6. When git prompts for password, paste the token

Verify at `https://github.com/your-username/ShanthiNikethan.EmployeeManagement`
— your files should be there.

## Step 4 — What NOT to commit

The `.gitignore` at the repo root already excludes:

- `bin/`, `obj/`, `publish/` (build outputs)
- `appsettings.Development.json`, `appsettings.Production.json` (contain secrets)
- `.pfx`, `.pem`, `.key`, `secrets.json`, `.env` (any secret file)
- `wwwroot/uploads/*` (staff photos are PII, keep them server-only)

**Never commit staff Aadhaar numbers, PAN, or passwords to the repo.**
The database holds those, and the database is not in the repo.

## Step 5 — Ongoing workflow with Claude Code

Once your repo is on GitHub, install Claude Code on your VM (see the
in-chat recommendation from Anthropic). It runs in your terminal or VS
Code and has direct filesystem + git access.

Typical session:

```powershell
cd C:\ShanthiNikethan
claude
```

Then talk to it naturally:

> "Add a phone-number format validator to the AddStaffDialog — Indian
> numbers, +91 optional prefix, exactly 10 digits."

Claude Code will read the relevant files, make the changes, and if you
approve, commit and push them. You get a proper git history of every
change made by AI, reviewable in GitHub.

For **design conversations** and larger scope changes (like "add an
Attendance module"), keep using this chat interface — I can see full
context and produce big scaffolds in one go. For **individual code
edits** against the existing repo, Claude Code is faster and cleaner.

## Step 6 — Branching for feature work (optional but recommended)

Once you're comfortable:

```powershell
# Start a new feature
git checkout -b feature/attendance-module

# ... make changes, commit as you go ...
git add .
git commit -m "Add attendance entity + service"

# Push the branch
git push -u origin feature/attendance-module

# On GitHub, open a Pull Request from the branch into main
# Review the diff, merge when happy
```

This gives you a review step before changes hit `main`, and rollback is
one click if a feature breaks something.

## Step 7 — Set up GitHub branch protection (recommended for production)

Once the app is deployed to production, protect `main`:

1. GitHub repo → **Settings → Branches → Add rule**
2. Branch name pattern: `main`
3. Enable:
   - ✅ Require a pull request before merging
   - ✅ Require conversation resolution before merging
4. Save

Now `main` can only be updated via reviewed PRs, which means Claude Code
(or anyone else) can't push a bad commit directly.

## What happens if I lose the token / GitHub account

- Personal Access Tokens can be regenerated any time from GitHub Settings.
- Your local git repo (`C:\ShanthiNikethan\.git`) contains the full
  history even without GitHub. You can always push it to a fresh remote.
- Keep a **weekly zip backup** of the repo folder as a belt-and-braces
  measure. `Compress-Archive -Path C:\ShanthiNikethan -DestinationPath
  D:\backups\repo-2026-08-02.zip`
