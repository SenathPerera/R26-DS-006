[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'status')]
    [string]$Action = 'start',

    [switch]$SkipWarmup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $repoRoot '.demo-runtime'
$statePath = Join-Path $runtimeDir 'backend-processes.json'
$script:startedProcesses = @()

$services = @(
    [pscustomobject]@{
        Name = 'Component B'
        HealthUri = 'http://127.0.0.1:8000/health'
        Python = Join-Path $repoRoot 'component-b\.venv\Scripts\python.exe'
        WorkingDirectory = Join-Path $repoRoot 'component-b'
        Module = 'server.main:app'
        Port = 8000
        Environment = @{ PYTHONPATH = 'src' }
    },
    [pscustomobject]@{
        Name = 'Session relay'
        HealthUri = 'http://127.0.0.1:8080/health'
        Python = Join-Path $repoRoot 'services\session_relay\.venv\Scripts\python.exe'
        WorkingDirectory = $repoRoot
        Module = 'services.session_relay.app:app'
        Port = 8080
        Environment = @{}
    },
    [pscustomobject]@{
        Name = 'Lyria backend'
        HealthUri = 'http://127.0.0.1:8002/health'
        Python = Join-Path $repoRoot 'services\lyria_backend\.venv\Scripts\python.exe'
        WorkingDirectory = Join-Path $repoRoot 'services\lyria_backend'
        Module = 'app:app'
        Port = 8002
        Environment = @{}
    },
    [pscustomobject]@{
        Name = 'Component D'
        HealthUri = 'http://127.0.0.1:8010/health'
        Python = Join-Path $repoRoot 'component-d\.venv\Scripts\python.exe'
        WorkingDirectory = Join-Path $repoRoot 'component-d'
        Module = 'server.main:app'
        Port = 8010
        Environment = @{
            PYTHONPATH = 'src'
            COMPONENT_B_URL = 'http://127.0.0.1:8000'
            OLLAMA_MODEL = 'qwen2.5:3b'
        }
    }
)

function Test-HttpEndpoint {
    param([Parameter(Mandatory)][string]$Uri)

    try {
        $null = Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 3
        return $true
    }
    catch {
        return $false
    }
}

function Save-ProcessState {
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    [pscustomobject]@{
        writtenAt = (Get-Date).ToString('o')
        processes = @($script:startedProcesses)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

function Start-BackendService {
    param([Parameter(Mandatory)]$Service)

    if (Test-HttpEndpoint -Uri $Service.HealthUri) {
        Write-Host "[ready] $($Service.Name) is already running on port $($Service.Port)." -ForegroundColor Green
        return
    }

    if (-not (Test-Path -LiteralPath $Service.Python)) {
        throw "Missing virtual environment for $($Service.Name): $($Service.Python)"
    }

    $savedEnvironment = @{}
    foreach ($key in $Service.Environment.Keys) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $Service.Environment[$key], 'Process')
    }

    $safeName = $Service.Name.ToLowerInvariant().Replace(' ', '-')
    $stdoutPath = Join-Path $runtimeDir "$safeName.stdout.log"
    $stderrPath = Join-Path $runtimeDir "$safeName.stderr.log"
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null

    try {
        $process = Start-Process `
            -FilePath $Service.Python `
            -ArgumentList @('-m', 'uvicorn', $Service.Module, '--host', '0.0.0.0', '--port', [string]$Service.Port) `
            -WorkingDirectory $Service.WorkingDirectory `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -WindowStyle Hidden `
            -PassThru
    }
    finally {
        foreach ($key in $Service.Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process')
        }
    }

    $script:startedProcesses += [pscustomobject]@{
        name = $Service.Name
        id = $process.Id
        executable = $Service.Python
        healthUri = $Service.HealthUri
    }
    Save-ProcessState

    Write-Host "[start] $($Service.Name) (PID $($process.Id)); waiting for health..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddMinutes(3)
    while ((Get-Date) -lt $deadline) {
        if (Test-HttpEndpoint -Uri $Service.HealthUri) {
            Write-Host "[ready] $($Service.Name)" -ForegroundColor Green
            return
        }
        if ($process.HasExited) {
            $tail = if (Test-Path -LiteralPath $stderrPath) {
                (Get-Content -LiteralPath $stderrPath -Tail 20) -join [Environment]::NewLine
            } else {
                '(no error log was produced)'
            }
            throw "$($Service.Name) exited before becoming healthy.`n$tail"
        }
        Start-Sleep -Seconds 2
    }

    throw "$($Service.Name) did not become healthy within three minutes. See $stderrPath"
}

function Show-BackendStatus {
    $checks = @(
        @{ Name = 'Ollama'; Uri = 'http://127.0.0.1:11434/api/tags' }
    )
    foreach ($service in $services) {
        $checks += @{ Name = $service.Name; Uri = $service.HealthUri }
    }

    foreach ($check in $checks) {
        if (Test-HttpEndpoint -Uri $check.Uri) {
            Write-Host ("[ready] {0,-15} {1}" -f $check.Name, $check.Uri) -ForegroundColor Green
        }
        else {
            Write-Host ("[down]  {0,-15} {1}" -f $check.Name, $check.Uri) -ForegroundColor Red
        }
    }
}

function Stop-TrackedBackends {
    if (-not (Test-Path -LiteralPath $statePath)) {
        Write-Host 'No tracked backend processes were found.' -ForegroundColor Yellow
        Show-BackendStatus
        return
    }

    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $tracked = @($state.processes)
    [array]::Reverse($tracked)

    foreach ($entry in $tracked) {
        $process = Get-Process -Id $entry.id -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            Write-Host "[gone] $($entry.name) (PID $($entry.id))"
            continue
        }

        $actualPath = $process.Path
        if ($actualPath -and ([IO.Path]::GetFullPath($actualPath) -ne [IO.Path]::GetFullPath($entry.executable))) {
            Write-Warning "PID $($entry.id) now belongs to another executable; it was not stopped."
            continue
        }

        Stop-Process -Id $entry.id
        Write-Host "[stop] $($entry.name) (PID $($entry.id))" -ForegroundColor Cyan
    }

    Remove-Item -LiteralPath $statePath -Force
}

switch ($Action) {
    'start' {
        if (-not (Test-HttpEndpoint -Uri 'http://127.0.0.1:11434/api/tags')) {
            throw 'Ollama is not reachable on port 11434. Start the Ollama application, then rerun this command.'
        }
        Write-Host '[ready] Ollama' -ForegroundColor Green

        foreach ($service in $services) {
            Start-BackendService -Service $service
        }

        if (-not $SkipWarmup) {
            Write-Host '[warmup] Loading Component D scorer and speech encoder. This may take several minutes...' -ForegroundColor Cyan
            $warmup = Invoke-RestMethod -Uri 'http://127.0.0.1:8010/warmup' -Method Post -TimeoutSec 900
            $warmupMessage = "Component D warmup: scorer=$($warmup.warmed.scorer), stt=$($warmup.warmed.stt)"
            if ($warmup.warmed.scorer -and $warmup.warmed.stt) {
                Write-Host "[ready] $warmupMessage" -ForegroundColor Green
            }
            else {
                Write-Warning "$warmupMessage. Check .demo-runtime\component-d.stderr.log before the demo."
            }
        }

        Write-Host "`nAll demo backends are ready. Logs: $runtimeDir" -ForegroundColor Green
        Show-BackendStatus
    }
    'stop' {
        Stop-TrackedBackends
    }
    'status' {
        Show-BackendStatus
    }
}
