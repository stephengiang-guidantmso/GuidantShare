<#
.SYNOPSIS
    Builds GuidantShare for production on the Windows VM: the API and the web app, into separate folders.
.DESCRIPTION
    API -> <Root>\api   (run by the GuidantShareApi Windows service)
    Web -> <Root>\web   (static files served by Caddy)
    Does not touch <Root>\api\data (database and encryption key) when updating an existing install.
    Written without being run on the VM; read it before use.
.EXAMPLE
    .\publish-windows.ps1
    .\publish-windows.ps1 -Root D:\Services\GuidantShare
#>
[CmdletBinding()]
param(
    [string]$Root = "C:\Services\GuidantShare"
)

$ErrorActionPreference = "Stop"
$Repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ApiOut = Join-Path $Root "api"
$WebOut = Join-Path $Root "web"

# 1. Web app (a build writes the files; emptyOutDir only clears the folder given here, never the API)
Push-Location (Join-Path $Repo "src\GuidantShare.Web")
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    npm run build -- --outDir $WebOut --emptyOutDir
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
} finally { Pop-Location }

# 2. API (the service must be stopped first if it is already installed, or files are locked)
$svc = Get-Service -Name "GuidantShareApi" -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq "Running") { Stop-Service -Name "GuidantShareApi" -Force; $restart = $true }

dotnet publish (Join-Path $Repo "src\GuidantShare.Api") -c Release -r win-x64 --self-contained false -o $ApiOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($restart) { Start-Service -Name "GuidantShareApi" }
Write-Host "Published. API: $ApiOut   Web: $WebOut"
Write-Host "Check: Invoke-RestMethod http://127.0.0.1:5080/health"
