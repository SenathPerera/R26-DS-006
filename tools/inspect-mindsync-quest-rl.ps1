[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$')]
    [string]$PackageName = 'com.mindsyncvr.templepond',
    [string]$AdbPath = '',
    [ValidatePattern('^[A-Za-z0-9._:-]{1,128}$')]
    [string]$DeviceSerial,
    [ValidateRange(1, 100)]
    [int]$MaxOutcomes = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

function Get-OutcomeCategory {
    param([string]$Reason)
    switch -Regex ($Reason) {
        'capability|domain|profile|reinitializ' { return 'Execution context changed' }
        'quality|confidence|unreliable|heart rate|RMSSD' { return 'Input reliability' }
        'stale|future|malformed' { return 'Input timing/format' }
        'timeout|before timeout' { return 'Outcome timeout' }
        'visual' { return 'Visual transition/interference' }
        'playback|PCM|clip|crossfade|Lyria|music|generation|stream' { return 'Audio playback/content change' }
        'pause|mute|disabled|session|network' { return 'Session/lifecycle interruption' }
        'coordinat|reservation' { return 'Shared turn cancelled' }
        default { return 'Other runtime reason' }
    }
}

function Get-PublicOutcome {
    param($Record)
    if ($null -eq $Record) { return }
    foreach ($field in @('eventType', 'rewardValid', 'onlineUpdateApplied', 'createdUtc', 'outcomeReason', 'executedArm')) {
        if ($null -eq $Record.PSObject.Properties[$field]) { return }
    }
    if ($Record.eventType -ne 'outcome') { return }
    if ($Record.rewardValid -isnot [bool] -or $Record.onlineUpdateApplied -isnot [bool]) { return }
    $timestamp = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$Record.createdUtc, [ref]$timestamp)) { return }
    $arm = 0
    if (-not [int]::TryParse([string]$Record.executedArm, [ref]$arm)) { return }
    $reason = [string]$Record.outcomeReason
    foreach ($field in @('sessionId', 'userId', 'decisionId')) {
        $property = $Record.PSObject.Properties[$field]
        if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            $reason = $reason.Replace([string]$property.Value, '[redacted]')
        }
    }
    $reason = [regex]::Replace($reason, 'https?://\S+|[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}', '[redacted]')
    $reason = [regex]::Replace($reason, '[\x00-\x1F\x7F]', ' ')
    if ($reason.Length -gt 220) { $reason = $reason.Substring(0, 220) }
    [pscustomobject]@{
        utc = $timestamp.ToUniversalTime().ToString('o')
        valid = [bool]$Record.rewardValid
        learned = [bool]$Record.onlineUpdateApplied
        arm = $arm
        category = if ($Record.rewardValid) { 'Accepted outcome' } else { Get-OutcomeCategory $reason }
        reason = $reason
    }
}

function Get-SessionLabel {
    param([string]$SessionId)
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($SessionId))
        return ([BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()).Substring(0, 10)
    }
    finally { $hash.Dispose() }
}

function Find-Adb {
    if ($AdbPath) { return (Resolve-Path -LiteralPath $AdbPath).Path }
    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @()
    foreach ($sdkRoot in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)) {
        if ($sdkRoot) { $candidates += Join-Path $sdkRoot 'platform-tools/adb.exe' }
    }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe' }
    if ($env:ProgramFiles) {
        $editors = Join-Path $env:ProgramFiles 'Unity/Hub/Editor'
        if (Test-Path $editors) {
            foreach ($editor in Get-ChildItem -LiteralPath $editors -Directory) {
                $candidates += Join-Path $editor.FullName 'Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
            }
        }
    }
    foreach ($candidate in $candidates) { if (Test-Path -LiteralPath $candidate) { return $candidate } }
    throw 'ADB not found. Pass -AdbPath with the adb.exe path from Android SDK platform-tools.'
}

function Invoke-QuestAdb {
    param([string[]]$Arguments)
    return Invoke-LocalAdb -Arguments (@('-s', $script:serial) + $Arguments)
}

function Invoke-LocalAdb {
    param([string[]]$Arguments)
    # ADB startup notices on stderr must not become terminating errors in Windows PowerShell 5.1.
    $ErrorActionPreference = 'Continue'
    $output = @(& $script:adb @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'Quest read failed. Check USB debugging authorization and the installed package name.' }
    return $output
}

Write-Output 'MindSync Quest RL inspection (read-only)'
Write-Output 'No keys, raw physiology, participant/session identities, or log filenames are printed.'
Write-Output 'Invalid means the outcome was discarded; no reward update was applied.'
$sessionLabel = ''
$failureHint = 'ADB discovery failed. Pass -AdbPath with the adb.exe path from Android SDK platform-tools.'
try {
    $script:adb = Find-Adb
    $failureHint = 'Connect and authorize the Quest for USB debugging, then rerun. Use -DeviceSerial if several Quests are connected.'
    $deviceLines = @(Invoke-LocalAdb -Arguments @('devices'))
    $devices = @($deviceLines | Where-Object { $_ -match '^\S+\s+device\s*$' } | ForEach-Object { ($_ -split '\s+')[0] })
    if ($DeviceSerial) {
        if ($devices -notcontains $DeviceSerial) { throw 'Requested device is not connected and authorized.' }
        $script:serial = $DeviceSerial
    }
    else {
        $quests = @()
        foreach ($device in $devices) {
            $model = (Invoke-LocalAdb -Arguments @('-s', $device, 'shell', 'getprop', 'ro.product.model')) -join ' '
            if ($model -match 'Quest') { $quests += $device }
        }
        if ($quests.Count -ne 1) { throw 'Connect and authorize one Quest, or select it using -DeviceSerial. No device identity is printed.' }
        $script:serial = $quests[0]
    }
    $failureHint = 'No readable Quest RL log. Start a session and verify -PackageName matches the installed APK.'
    $directory = "/sdcard/Android/data/$PackageName/files/RLTransitions"
    $paths = @(Invoke-QuestAdb -Arguments @('shell', "ls -t $directory/audio_rl_transitions_*.jsonl") | ForEach-Object { $_.Trim() })
    $pattern = '^' + [regex]::Escape($directory) + '/audio_rl_transitions_[A-Za-z0-9_.-]+\.jsonl$'
    $latest = $paths | Where-Object { $_ -match $pattern } | Select-Object -First 1
    if (-not $latest) { throw 'No readable audio RL log. Start a session and confirm the package name matches the installed APK.' }
    $records = @()
    foreach ($line in @(Invoke-QuestAdb -Arguments @('shell', "tail -n 500 $latest"))) {
        try {
            $record = $line | ConvertFrom-Json -ErrorAction Stop
            if ($record.schemaVersion -eq 2 -and $record.eventType -in @('decision', 'outcome')) { $records += $record }
        }
        catch { }
    }
    if ($records.Count -gt 0 -and $records[-1].sessionId) { $sessionLabel = Get-SessionLabel $records[-1].sessionId }
    $outcomes = @($records | ForEach-Object { Get-PublicOutcome $_ } | Select-Object -Last $MaxOutcomes)
    Write-Output ("[DATA] Quest recent log tail: decisions={0}; outcomes={1}; valid={2}; invalid={3}" -f
        @($records | Where-Object eventType -eq 'decision').Count, $outcomes.Count,
        @($outcomes | Where-Object valid).Count, @($outcomes | Where-Object { -not $_.valid }).Count)
    foreach ($outcome in $outcomes) {
        Write-Output ("[OUTCOME] UTC={0}; valid={1}; learned={2}; executedArm={3}; category={4}; reason={5}" -f
            $outcome.utc, $outcome.valid, $outcome.learned, $outcome.arm, $outcome.category, $outcome.reason)
    }
    if ($outcomes.Count -eq 0) { Write-Output '[INFO] No delayed outcome in this log tail yet; collecting baseline or waiting is possible.' }
    Write-Output '[INFO] Log events are historical. The relay snapshot below determines whether this same session is currently live.'
}
catch { Write-Output ("[WARN] Quest outcome inspection unavailable: {0}" -f $failureHint) }

# Reuse the existing sanitized relay report without executing the full diagnostic script.
$source = Join-Path $repo 'tools/diagnose-mindsync-windows.ps1'
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Existing diagnostic script failed syntax validation.' }
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
$RepoRoot = $repo
$script:Report = New-Object 'System.Collections.Generic.List[string]'
$script:PassCount = $script:WarnCount = $script:FailCount = 0
try {
    if (-not $sessionLabel) { Write-Output '[WARN] No Quest log session was matched. The relay report may belong to another active session.' }
    $snapshot = Get-DiagnosticsSnapshot -SessionLabel $sessionLabel
    if ($sessionLabel -and $null -eq $snapshot.Session) {
        Write-Output '[WARN] The Quest log session is not present in relay diagnostics; it may be an earlier session, or the relay is unavailable.'
    }
    Show-AgentSnapshot $snapshot
    $script:Report | Write-Output
}
catch { Write-Output '[WARN] Relay snapshot unavailable. Run with the session relay and backends active on this PC.' }
