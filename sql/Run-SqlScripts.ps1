param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("DEV", "E2E")]
    [string]$Environment,

    [Parameter(Mandatory=$true)]
    [string]$DatabaseName,

    [string]$Server = ".\SQLEXPRESS",

    [string]$SqlFolder = "$PSScriptRoot\"
)

# Confirms sqlcmd is actually available before doing anything else - a
# clear, early failure here beats a confusing one halfway through.
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Error "sqlcmd not found. Install the 'SQL Server Command Line Utilities': https://learn.microsoft.com/sql/tools/sqlcmd-utility"
    exit 1
}

if (-not (Test-Path $SqlFolder)) {
    Write-Error "SQL folder not found at '$SqlFolder' - pass -SqlFolder explicitly if your scripts live somewhere else."
    exit 1
}

# Matches the established numbering convention (01-, 02-, ... 31-, 32-)
# and deliberately includes script 32 ONLY for E2E - a DEV database
# should never get the E2E-only test account seeded into it.
$allScripts = Get-ChildItem -Path $SqlFolder -Filter "*.sql" | Sort-Object Name
$scriptsToRun = $allScripts | Where-Object {
    if ($_.Name -match '^(\d{2})-') {
        $num = [int]$matches[1]
        if ($Environment -eq "DEV") { $num -le 31 }
        else { $num -le 32 }
    } else {
        $false  # anything not matching the numbered-prefix convention is skipped, not guessed at
    }
}

if ($scriptsToRun.Count -eq 0) {
    Write-Error "No matching numbered .sql scripts found in '$SqlFolder' - check the path and filenames."
    exit 1
}

Write-Host "About to run $($scriptsToRun.Count) script(s) against '$DatabaseName' on '$Server' ($Environment mode):" -ForegroundColor Yellow
$scriptsToRun | ForEach-Object { Write-Host "  - $($_.Name)" }
Write-Host ""

if ($Environment -eq "E2E" -and ($scriptsToRun | Where-Object { $_.Name -match '^32-' })) {
    Write-Host "Reminder: script 32 needs a real password hash pasted in first (via HashGenerator)." -ForegroundColor Yellow
    Write-Host "If you haven't done that yet, Ctrl+C now and come back once it's ready." -ForegroundColor Yellow
    Write-Host ""
}

foreach ($script in $scriptsToRun) {
    Write-Host "Running $($script.Name)..." -ForegroundColor Cyan

    # Each script has its own hardcoded "USE ShanthiNikethanEmployeeManagement_DEV;"
    # at the top - fine for manual SSMS use, but it silently overrides
    # sqlcmd's -d flag the moment the script runs, meaning every script
    # would otherwise always target _DEV regardless of what was actually
    # requested. This rewrites just that one line, in a temporary copy,
    # leaving the real, original script file completely untouched.
    $content = Get-Content -Path $script.FullName -Raw
    $rewritten = $content -replace '(?im)^\s*USE\s+\[?[\w]+\]?\s*;?.*$', "USE [$DatabaseName];"
    $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
    Set-Content -Path $tempFile -Value $rewritten -Encoding UTF8

    # -E: Windows integrated authentication, matching this project's own
    #     connection strings (Trusted_Connection=True) throughout.
    # -C: trusts the server's certificate - a local SQLEXPRESS instance
    #     uses a self-signed certificate, and ODBC Driver 18 changed its
    #     default to require a CA-signed one, so this is needed here for
    #     the exact same reason TrustServerCertificate=True already
    #     appears in every .NET connection string in this project.
    # -I: sets QUOTED_IDENTIFIER ON - sqlcmd's own default is OFF, unlike
    #     SSMS's query editor which defaults to ON. Several of these
    #     scripts create filtered indexes or indexes on computed columns,
    #     which SQL Server specifically requires QUOTED_IDENTIFIER ON for -
    #     this never surfaced before because these scripts were always
    #     run through SSMS previously, never through sqlcmd.
    # -b: makes sqlcmd exit with a non-zero code on a real SQL error -
    #     without this, SQL Server's own default "keep going after an
    #     error" behavior (the exact thing that caused a real, confusing
    #     bug earlier in this project's E2E seed script) would let this
    #     loop silently continue past a genuine failure.
    sqlcmd -S $Server -d $DatabaseName -E -C -I -b -i $tempFile

    $exitCode = $LASTEXITCODE
    Remove-Item -Path $tempFile -ErrorAction SilentlyContinue

    if ($exitCode -ne 0) {
        Write-Error "STOPPED: $($script.Name) failed (exit code $exitCode)."
        Write-Error "Fix the issue before re-running - later scripts assume everything before them already succeeded."
        exit 1
    }

    Write-Host "  OK" -ForegroundColor Green
}

Write-Host ""
Write-Host "All $($scriptsToRun.Count) script(s) completed successfully against '$DatabaseName'." -ForegroundColor Green
