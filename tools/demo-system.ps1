[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'status')]
    [string]$Action = 'start',

    [string]$Device,

    [switch]$SkipWarmup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$mobileRoot = Join-Path $repoRoot 'MindSync-VR\react-native-app'
$backendScript = Join-Path $PSScriptRoot 'demo-backends.ps1'
$runtimeDir = Join-Path $repoRoot '.demo-runtime'
$mobileStatePath = Join-Path $runtimeDir 'mobile-processes.json'
$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'

function Test-Metro {
    try {
        $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8081/status' -UseBasicParsing -TimeoutSec 3
        $content = if ($response.Content -is [byte[]]) {
            [Text.Encoding]::UTF8.GetString($response.Content)
        }
        else {
            [string]$response.Content
        }
        return $content -match 'packager-status:running'
    }
    catch {
        return $false
    }
}

function Get-AndroidDevice {
    if (-not (Test-Path -LiteralPath $adb)) {
        throw "ADB was not found at $adb. Install Android SDK Platform-Tools first."
    }

    $connected = @(
        & $adb devices |
            Where-Object { $_ -match '^([^\s]+)\s+device$' } |
            ForEach-Object { ([regex]::Match($_, '^([^\s]+)')).Groups[1].Value }
    )

    if ($Device) {
        if ($connected -notcontains $Device) {
            throw "Android device '$Device' is not connected and authorized. Connected devices: $($connected -join ', ')"
        }
        return $Device
    }

    if ($connected.Count -eq 0) {
        throw 'No authorized Android device is connected. Connect and unlock the phone, then rerun the command.'
    }
    if ($connected.Count -gt 1) {
        throw "Multiple Android devices are connected. Rerun with -Device <serial>. Devices: $($connected -join ', ')"
    }
    return $connected[0]
}

function Save-MobileState {
    param([Parameter(Mandatory)][int]$MetroProcessId)

    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    [pscustomobject]@{
        writtenAt = (Get-Date).ToString('o')
        metroProcessId = $MetroProcessId
        mobileRoot = $mobileRoot
    } | ConvertTo-Json | Set-Content -LiteralPath $mobileStatePath -Encoding UTF8
}

function Start-Metro {
    if (Test-Metro) {
        Write-Host '[ready] Metro is already running on port 8081.' -ForegroundColor Green
        return
    }

    Write-Host '[start] Opening Metro in a dedicated terminal...' -ForegroundColor Cyan
    $process = Start-Process `
        -FilePath 'cmd.exe' `
        -ArgumentList @('/k', 'npm.cmd start -- --reset-cache') `
        -WorkingDirectory $mobileRoot `
        -WindowStyle Normal `
        -PassThru
    Save-MobileState -MetroProcessId $process.Id

    $deadline = (Get-Date).AddMinutes(2)
    while ((Get-Date) -lt $deadline) {
        if (Test-Metro) {
            Write-Host '[ready] Metro' -ForegroundColor Green
            return
        }
        if ($process.HasExited) {
            throw 'Metro exited before becoming ready. Review the Metro terminal for the error.'
        }
        Start-Sleep -Seconds 2
    }
    throw 'Metro did not become ready within two minutes. Review the Metro terminal.'
}

function Stop-TrackedMetro {
    if (-not (Test-Path -LiteralPath $mobileStatePath)) {
        Write-Host '[skip] No Metro process launched by this script is tracked.' -ForegroundColor Yellow
        return
    }

    $state = Get-Content -LiteralPath $mobileStatePath -Raw | ConvertFrom-Json
    $process = Get-Process -Id $state.metroProcessId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        $allowedNames = @('cmd', 'powershell', 'pwsh')
        if ($allowedNames -notcontains $process.ProcessName) {
            Write-Warning "Tracked PID $($state.metroProcessId) is now '$($process.ProcessName)' and was not stopped."
        }
        else {
            & taskkill.exe /PID $state.metroProcessId /T /F | Out-Null
            Write-Host "[stop] Metro process tree (PID $($state.metroProcessId))" -ForegroundColor Cyan
        }
    }
    Remove-Item -LiteralPath $mobileStatePath -Force
}

function Show-SystemStatus {
    & $backendScript status
    if (Test-Metro) {
        Write-Host '[ready] Metro           http://127.0.0.1:8081' -ForegroundColor Green
    }
    else {
        Write-Host '[down]  Metro           http://127.0.0.1:8081' -ForegroundColor Red
    }

    if (Test-Path -LiteralPath $adb) {
        $devices = @(& $adb devices | Where-Object { $_ -match '\sdevice$' })
        if ($devices.Count -gt 0) {
            Write-Host "[ready] Android device  $($devices -join ', ')" -ForegroundColor Green
        }
        else {
            Write-Host '[down]  Android device  none connected' -ForegroundColor Red
        }
    }
    else {
        Write-Host '[down]  ADB             platform-tools not found' -ForegroundColor Red
    }
}

switch ($Action) {
    'start' {
        if ($SkipWarmup) {
            & $backendScript start -SkipWarmup
        }
        else {
            & $backendScript start
        }

        $selectedDevice = Get-AndroidDevice
        Write-Host "[ready] Android device $selectedDevice" -ForegroundColor Green

        & $adb -s $selectedDevice reverse tcp:8081 tcp:8081 | Out-Null
        & $adb -s $selectedDevice reverse tcp:8010 tcp:8010 | Out-Null
        Write-Host '[ready] ADB forwarding for Metro (8081) and Component D (8010)' -ForegroundColor Green

        Start-Metro

        & $adb -s $selectedDevice shell am force-stop com.mindsyncvr
        $launchOutput = & $adb -s $selectedDevice shell am start -n com.mindsyncvr/.MainActivity 2>&1
        if ($LASTEXITCODE -ne 0 -or $launchOutput -match 'Error type|does not exist') {
            throw "The mobile app could not be launched. Build/install it once, then rerun this command.`n$($launchOutput -join [Environment]::NewLine)"
        }

        Write-Host '[ready] MindSync mobile app launched.' -ForegroundColor Green
        Write-Host "`nACTION 1 COMPLETE" -ForegroundColor Green
        Write-Host 'ACTION 2: Start the prepared Unity scene in the Editor, or launch the installed Quest application.' -ForegroundColor Yellow
    }
    'stop' {
        $selectedDevice = $null
        try { $selectedDevice = Get-AndroidDevice } catch { Write-Warning $_.Exception.Message }
        if ($selectedDevice) {
            & $adb -s $selectedDevice shell am force-stop com.mindsyncvr
            & $adb -s $selectedDevice reverse --remove tcp:8081 2>$null
            & $adb -s $selectedDevice reverse --remove tcp:8010 2>$null
            Write-Host '[stop] MindSync mobile app and ADB forwarding' -ForegroundColor Cyan
        }
        Stop-TrackedMetro
        & $backendScript stop
    }
    'status' {
        Show-SystemStatus
    }
}
