<#
.SYNOPSIS
    Registers GuidantShare.Api as the Windows service "GuidantShareApi" and sets its environment (run once, as Administrator).
.DESCRIPTION
    The FileSync API key and the Graph mail secret are read from the prompt and stored only in the service's registry key
    (readable by Administrators and SYSTEM), never in a file in the repo. Re-run to change them.
    Written without being run on the VM; read it before use.
.EXAMPLE
    .\install-api-service.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = "C:\Services\GuidantShare",
    [string]$ServiceName = "GuidantShareApi",
    [string]$FileSyncBaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $Root "api\GuidantShare.Api.exe"
if (-not (Test-Path $exe)) { throw "Not found: $exe. Run publish-windows.ps1 first." }

function Read-Secret([string]$prompt) {
    $s = Read-Host -Prompt $prompt -AsSecureString
    [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}

$fileSyncKey = Read-Secret "FileSync API key (the same value as FileSync's Api__Key)"
if ([string]::IsNullOrWhiteSpace($fileSyncKey)) { throw "The FileSync API key is required." }

$vars = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "FileSync__BaseUrl=$FileSyncBaseUrl",
    "FileSync__ApiKey=$fileSyncKey"
)

$useGraph = Read-Host "Send invite emails with Microsoft Graph? (y/N)"
if ($useGraph -match '^[yY]') {
    $vars += "Email__Mode=Graph"
    $vars += "Email__TenantId=$(Read-Host 'Tenant ID')"
    $vars += "Email__ClientId=$(Read-Host 'Client (app) ID')"
    $vars += "Email__Mailbox=$(Read-Host 'Sending mailbox')"
    $vars += "Email__ClientSecret=$(Read-Secret 'Graph client secret')"
}

if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName "GuidantShare API" `
        -Description "GuidantShare public API (listens on 127.0.0.1:5080 behind Caddy)" -StartupType Automatic | Out-Null
    # restart on failure after 10 s, 30 s, 60 s
    sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
    Write-Host "Service $ServiceName created."
}

# A service reads its environment from this multi-string value
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name "Environment" -Type MultiString -Value $vars

Restart-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ((Get-Service -Name $ServiceName).Status -ne "Running") { Start-Service -Name $ServiceName }
Write-Host "Running. Check: Invoke-RestMethod http://127.0.0.1:5080/health"
Write-Host "First time only: create the superadmin. Run it FROM the api folder so it uses the same data\ folder as the service:"
Write-Host "  cd $Root\api"
Write-Host "  .\GuidantShare.Api.exe create-superadmin --email you@example.com"
