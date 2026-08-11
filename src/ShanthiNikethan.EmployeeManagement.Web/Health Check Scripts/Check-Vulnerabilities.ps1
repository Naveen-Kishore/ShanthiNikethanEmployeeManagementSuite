<#
.SYNOPSIS
    Scans every NuGet dependency (direct and transitive) for known
    vulnerabilities and produces a clean, self-contained HTML report.

.DESCRIPTION
    Wraps `dotnet list package --vulnerable --include-transitive --format json`,
    parses the results, and generates a styled HTML report with a severity
    breakdown, per-project package tables, advisory links, and suggested
    next steps for each finding. Intended to be run manually, roughly once
    a month, to check whether previously-flagged issues have been patched
    and whether anything new has appeared.

    IMPORTANT: this script's JSON parsing was written against the
    documented, stable shape of `dotnet list package --format json`
    (available since the .NET 8 SDK), but has not been run against a real
    solution to confirm the exact field names match on this machine's SDK
    version. The first real run should be checked carefully - if parsing
    fails, the script prints the raw JSON so the actual field names can be
    compared against what this script expects.

.PARAMETER SolutionPath
    Path to the .sln file. If not given, looks for a single .sln file in
    the current directory.

.PARAMETER OutputDirectory
    Where to save the HTML report. Defaults to .\vulnerability-reports,
    created if it doesn't exist. Each run is saved with a timestamped
    filename, so monthly runs build up a history rather than overwriting
    each other.

.EXAMPLE
    .\Check-Vulnerabilities.ps1

.EXAMPLE
    .\Check-Vulnerabilities.ps1 -SolutionPath "C:\ShanthiNikethanEmployeeManagementSuite-DEV\src\ShanthiNikethan.EmployeeManagement.sln"
#>

param(
    [string]$SolutionPath = "",
    [string]$OutputDirectory = ".\vulnerability-reports"
)

$ErrorActionPreference = "Stop"

# ----------------------------------------------------------------------
# Locate the solution
# ----------------------------------------------------------------------
if (-not $SolutionPath) {
    $found = Get-ChildItem -Filter "*.sln" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        $SolutionPath = $found.FullName
    }
}

if (-not $SolutionPath -or -not (Test-Path $SolutionPath)) {
    Write-Host "No .sln file found." -ForegroundColor Red
    Write-Host "Run this script from the folder containing your solution, or pass it explicitly:" -ForegroundColor Yellow
    Write-Host "  .\Check-Vulnerabilities.ps1 -SolutionPath `"C:\path\to\YourSolution.sln`"" -ForegroundColor Gray
    exit 1
}

Write-Host ""
Write-Host "Scanning: $SolutionPath" -ForegroundColor Cyan
Write-Host ""

# ----------------------------------------------------------------------
# Restore first - `dotnet list package --vulnerable` reports nothing
# useful (or errors outright) against an unrestored project.
# ----------------------------------------------------------------------
Write-Host "Restoring packages..." -ForegroundColor Gray
dotnet restore $SolutionPath 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet restore failed - fix that first, since an unrestored solution can't be scanned reliably." -ForegroundColor Red
    exit 1
}

Write-Host "Checking dependencies against known vulnerability advisories (this can take a minute or two)..." -ForegroundColor Gray
$jsonOutput = dotnet list $SolutionPath package --vulnerable --include-transitive --format json 2>&1
$jsonText = ($jsonOutput | Out-String).Trim()

if ([string]::IsNullOrWhiteSpace($jsonText)) {
    Write-Host "No output at all from 'dotnet list package --vulnerable --format json'." -ForegroundColor Red
    Write-Host "This SDK version may not support --format json - it requires .NET 8 SDK or later. Check with: dotnet --version" -ForegroundColor Yellow
    exit 1
}

try {
    $report = $jsonText | ConvertFrom-Json
} catch {
    Write-Host "Couldn't parse the JSON output - showing the raw output below so the actual structure can be compared against what this script expects:" -ForegroundColor Red
    Write-Host $jsonText
    exit 1
}

# ----------------------------------------------------------------------
# Walk the parsed structure and flatten every finding into one list.
# Defensive throughout (checking properties exist before reading them)
# since this is exactly the part that couldn't be verified against a
# real run in advance.
# ----------------------------------------------------------------------
$findings = @()

if ($report.PSObject.Properties.Name -contains "projects") {
    foreach ($project in $report.projects) {
        $projectName = if ($project.PSObject.Properties.Name -contains "path") {
            Split-Path $project.path -Leaf
        } else { "(unknown project)" }

        if ($project.PSObject.Properties.Name -notcontains "frameworks") { continue }

        foreach ($fw in $project.frameworks) {
            $frameworkName = if ($fw.PSObject.Properties.Name -contains "framework") { $fw.framework } else { "" }

            foreach ($kind in @("topLevelPackages", "transitivePackages")) {
                if ($fw.PSObject.Properties.Name -notcontains $kind) { continue }
                $isDirect = $kind -eq "topLevelPackages"

                foreach ($pkg in $fw.$kind) {
                    if ($pkg.PSObject.Properties.Name -notcontains "vulnerabilities") { continue }
                    if (-not $pkg.vulnerabilities -or $pkg.vulnerabilities.Count -eq 0) { continue }

                    foreach ($vuln in $pkg.vulnerabilities) {
                        $findings += [PSCustomObject]@{
                            Project      = $projectName
                            Framework    = $frameworkName
                            PackageId    = $pkg.id
                            Version      = if ($pkg.PSObject.Properties.Name -contains "resolvedVersion") { $pkg.resolvedVersion } else { "?" }
                            IsDirect     = $isDirect
                            Severity     = if ($vuln.PSObject.Properties.Name -contains "severity") { $vuln.severity } else { "Unknown" }
                            AdvisoryUrl  = if ($vuln.PSObject.Properties.Name -contains "advisoryurl") { $vuln.advisoryurl } else { "" }
                        }
                    }
                }
            }
        }
    }
}

# ----------------------------------------------------------------------
# Console summary
# ----------------------------------------------------------------------
Write-Host ""
if ($findings.Count -eq 0) {
    Write-Host "No known vulnerabilities found." -ForegroundColor Green
} else {
    Write-Host "$($findings.Count) finding(s) across $(($findings.PackageId | Select-Object -Unique).Count) package(s)." -ForegroundColor Yellow
    $bySeverity = $findings | Group-Object Severity | Sort-Object Count -Descending
    foreach ($group in $bySeverity) {
        Write-Host "  $($group.Name): $($group.Count)" -ForegroundColor Gray
    }
}
Write-Host ""

# ----------------------------------------------------------------------
# HTML report
# ----------------------------------------------------------------------
if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmm"
$reportPath = Join-Path $OutputDirectory "vulnerability-report-$timestamp.html"
$scanDate = Get-Date -Format "dddd, dd MMMM yyyy 'at' hh:mm tt"

function Get-SeverityColor($severity) {
    switch ($severity) {
        "Critical" { return "#7a1414" }
        "High"     { return "#b42318" }
        "Moderate" { return "#b8860b" }
        "Low"      { return "#5c6b73" }
        default    { return "#5c6b73" }
    }
}

function Get-SeverityAction($severity, $isDirect) {
    $base = if ($isDirect) {
        "Update this package directly: <code>dotnet add package [PackageName] --version [patched version from the advisory link]</code>"
    } else {
        "This is a transitive (indirect) dependency - check the advisory for which direct package pulls it in, since updating that parent package is usually what actually resolves it."
    }
    switch ($severity) {
        "Critical" { return "Treat as urgent. $base" }
        "High"     { return "Address soon. $base" }
        default    { return $base }
    }
}

$severityOrder = @{ "Critical" = 0; "High" = 1; "Moderate" = 2; "Low" = 3; "Unknown" = 4 }
$sortedFindings = $findings | Sort-Object { $severityOrder[$_.Severity] }, PackageId

$summaryCardsHtml = ""
foreach ($sev in @("Critical", "High", "Moderate", "Low")) {
    $count = ($findings | Where-Object { $_.Severity -eq $sev }).Count
    $color = Get-SeverityColor $sev
    $summaryCardsHtml += @"
        <div class="summary-card" style="border-top-color: $color;">
            <div class="summary-count" style="color: $color;">$count</div>
            <div class="summary-label">$sev</div>
        </div>
"@
}

if ($findings.Count -eq 0) {
    $bodyHtml = @"
    <div class="all-clear">
        <div class="all-clear-icon">&#10003;</div>
        <h2>No known vulnerabilities found</h2>
        <p>Every scanned package - direct and transitive - came back clean against current advisory data.</p>
    </div>
"@
} else {
    $rowsHtml = ""
    foreach ($f in $sortedFindings) {
        $color = Get-SeverityColor $f.Severity
        $action = Get-SeverityAction $f.Severity $f.IsDirect
        $directLabel = if ($f.IsDirect) { "Direct" } else { "Transitive" }
        $advisoryLink = if ($f.AdvisoryUrl) { "<a href=`"$($f.AdvisoryUrl)`" target=`"_blank`">$($f.AdvisoryUrl)</a>" } else { "(no advisory URL provided)" }
        $rowsHtml += @"
        <tr>
            <td><span class="severity-badge" style="background: $color;">$($f.Severity)</span></td>
            <td><strong>$($f.PackageId)</strong></td>
            <td>$($f.Version)</td>
            <td>$directLabel</td>
            <td class="project-cell">$($f.Project)<br><span class="framework-cell">$($f.Framework)</span></td>
            <td>$advisoryLink</td>
            <td class="action-cell">$action</td>
        </tr>
"@
    }
    $bodyHtml = @"
    <table>
        <thead>
            <tr>
                <th>Severity</th>
                <th>Package</th>
                <th>Version</th>
                <th>Type</th>
                <th>Project</th>
                <th>Advisory</th>
                <th>Suggested action</th>
            </tr>
        </thead>
        <tbody>
            $rowsHtml
        </tbody>
    </table>
"@
}

$html = @"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>Dependency Vulnerability Report - $timestamp</title>
<style>
    * { box-sizing: border-box; }
    body {
        font-family: -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
        margin: 0; padding: 40px 24px; background: #f7f6f4; color: #1f1f1f;
    }
    .container { max-width: 1100px; margin: 0 auto; }
    h1 { font-size: 24px; margin: 0 0 4px 0; }
    .subtitle { color: #666; font-size: 14px; margin: 0 0 28px 0; }
    .summary-row { display: flex; gap: 16px; margin-bottom: 32px; flex-wrap: wrap; }
    .summary-card {
        background: #fff; border-radius: 10px; padding: 18px 24px; flex: 1; min-width: 120px;
        border-top: 4px solid #ccc; box-shadow: 0 1px 3px rgba(0,0,0,0.08);
    }
    .summary-count { font-size: 32px; font-weight: 700; line-height: 1; }
    .summary-label { font-size: 13px; color: #666; margin-top: 6px; text-transform: uppercase; letter-spacing: 0.03em; }
    table { width: 100%; border-collapse: collapse; background: #fff; border-radius: 10px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.08); }
    th { text-align: left; padding: 12px 14px; background: #2f2f2f; color: #fff; font-size: 12.5px; text-transform: uppercase; letter-spacing: 0.03em; }
    td { padding: 12px 14px; border-bottom: 1px solid #eee; font-size: 13.5px; vertical-align: top; }
    tr:last-child td { border-bottom: none; }
    .severity-badge { color: #fff; padding: 3px 10px; border-radius: 100px; font-size: 12px; font-weight: 600; white-space: nowrap; }
    .project-cell { font-size: 12.5px; }
    .framework-cell { color: #888; font-size: 11.5px; }
    .action-cell { font-size: 12.5px; color: #444; max-width: 280px; }
    code { background: #f0efec; padding: 1px 5px; border-radius: 4px; font-size: 12px; }
    a { color: #6d28d9; }
    .all-clear { background: #fff; border-radius: 10px; padding: 48px 24px; text-align: center; box-shadow: 0 1px 3px rgba(0,0,0,0.08); }
    .all-clear-icon { font-size: 40px; color: #1a9c5c; margin-bottom: 12px; }
    .all-clear h2 { margin: 0 0 8px 0; }
    .all-clear p { color: #666; margin: 0; }
    .footer { margin-top: 32px; font-size: 12.5px; color: #888; text-align: center; }
</style>
</head>
<body>
<div class="container">
    <h1>Dependency Vulnerability Report</h1>
    <p class="subtitle">Scanned $scanDate &middot; $SolutionPath</p>

    <div class="summary-row">
        $summaryCardsHtml
    </div>

    $bodyHtml

    <p class="footer">
        Generated by Check-Vulnerabilities.ps1 &middot; Re-run monthly with: <code>.\Check-Vulnerabilities.ps1</code><br />
        Data source: NuGet's vulnerability advisory feed (GitHub Advisory Database)
    </p>
</div>
</body>
</html>
"@

$html | Out-File -FilePath $reportPath -Encoding utf8

Write-Host "Report saved to: $reportPath" -ForegroundColor Cyan
Write-Host "Opening in your default browser..." -ForegroundColor Gray
Start-Process $reportPath
