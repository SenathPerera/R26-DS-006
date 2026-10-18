[CmdletBinding()]
param(
    [ValidateRange(0, 120)]
    [int]$ObserveSeconds = 20,

    [string]$RepoRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$script:Report = New-Object 'System.Collections.Generic.List[string]'
$script:PassCount = 0
$script:WarnCount = 0
$script:FailCount = 0

function Add-Line {
    param([string]$Text = '')
    [void]$script:Report.Add($Text)
}

function Add-Check {
    param(
        [ValidateSet('PASS', 'WARN', 'FAIL', 'INFO')]
        [string]$Level,
        [string]$Message
    )

    switch ($Level) {
        'PASS' { $script:PassCount++ }
        'WARN' { $script:WarnCount++ }
        'FAIL' { $script:FailCount++ }
    }
    Add-Line "[$Level] $Message"
}

function Get-NestedValue {
    param(
        [AllowNull()]$Object,
        [string[]]$Path,
        [AllowNull()]$Default = $null
    )

    $current = $Object
    foreach ($part in $Path) {
        if ($null -eq $current) {
            return $Default
        }
        if ($current -is [System.Collections.IDictionary]) {
            if (-not $current.Contains($part)) {
                return $Default
            }
            $current = $current[$part]
            continue
        }
        $property = $current.PSObject.Properties[$part]
        if ($null -eq $property) {
            return $Default
        }
        $current = $property.Value
    }
    if ($null -eq $current) {
        return $Default
    }
    return $current
}

function Get-Number {
    param(
        [AllowNull()]$Value,
        [double]$Default = 0
    )

    if ($null -eq $Value) {
        return $Default
    }
    $number = 0.0
    if ([double]::TryParse(
        [string]$Value,
        [Globalization.NumberStyles]::Any,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$number
    )) {
        return $number
    }
    return $Default
}

function Format-Value {
    param([AllowNull()]$Value)
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 'n/a'
    }
    return [string]$Value
}

function Format-Bool {
    param([AllowNull()]$Value)
    if ($null -eq $Value) {
        return 'n/a'
    }
    if ([bool]$Value) {
        return 'yes'
    }
    return 'no'
}

function Format-Vector {
    param([AllowNull()]$Value)
    if ($null -eq $Value) {
        return 'n/a'
    }
    $items = @($Value)
    if ($items.Count -eq 0) {
        return 'n/a'
    }
    $formatted = foreach ($item in $items) {
        '{0:0.###}' -f (Get-Number -Value $item)
    }
    return '[' + ($formatted -join ', ') + ']'
}

function Get-EpochSeconds {
    return ([DateTimeOffset]::UtcNow - [DateTimeOffset]'1970-01-01T00:00:00Z').TotalSeconds
}

function Format-Age {
    param([AllowNull()]$Timestamp)
    if ($null -eq $Timestamp) {
        return 'n/a'
    }
    $age = [Math]::Max(0, (Get-EpochSeconds) - (Get-Number -Value $Timestamp))
    return ('{0:0.0}s' -f $age)
}

function Invoke-JsonProbe {
    param(
        [string]$Uri,
        [int]$TimeoutSec = 4
    )

    try {
        $data = Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec $TimeoutSec
        return [pscustomobject]@{
            Ok = $true
            Data = $data
            Error = ''
        }
    }
    catch {
        return [pscustomobject]@{
            Ok = $false
            Data = $null
            Error = $_.Exception.Message
        }
    }
}

function Get-TcpConnections {
    param(
        [int]$Port,
        [string]$State
    )

    try {
        return @(Get-NetTCPConnection -LocalPort $Port -State $State -ErrorAction Stop)
    }
    catch {
        return @()
    }
}

function Get-ProcessLabel {
    param([int]$ProcessId)
    try {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        return "$($process.ProcessName) pid=$ProcessId"
    }
    catch {
        return "pid=$ProcessId"
    }
}

function Select-RelaySession {
    param([AllowNull()]$RelayMonitor)

    $sessionsValue = Get-NestedValue -Object $RelayMonitor -Path @('details', 'sessions') -Default @()
    $sessions = @($sessionsValue)
    if ($sessions.Count -eq 0) {
        return $null
    }
    $active = @($sessions | Where-Object {
        (Get-NestedValue -Object $_ -Path @('mobileConnected') -Default $false) -or
        (Get-NestedValue -Object $_ -Path @('questConnected') -Default $false)
    })
    if ($active.Count -gt 0) {
        return $active[0]
    }
    return $sessions[0]
}

function Get-DiagnosticsSnapshot {
    $componentBProbe = Invoke-JsonProbe -Uri 'http://127.0.0.1:8000/monitor/status'
    $relayProbe = Invoke-JsonProbe -Uri 'http://127.0.0.1:8080/monitor/status'
    $session = if ($relayProbe.Ok) { Select-RelaySession -RelayMonitor $relayProbe.Data } else { $null }
    $agentProbe = $null

    if ($null -ne $session) {
        $label = Get-NestedValue -Object $session -Path @('label')
        if (-not [string]::IsNullOrWhiteSpace([string]$label)) {
            $escaped = [Uri]::EscapeDataString([string]$label)
            $agentProbe = Invoke-JsonProbe -Uri "http://127.0.0.1:8080/monitor/agents?session=$escaped"
        }
    }

    $agentSample = $null
    if ($null -ne $agentProbe -and $agentProbe.Ok) {
        $samples = @(Get-NestedValue -Object $agentProbe.Data -Path @('samples') -Default @())
        if ($samples.Count -gt 0) {
            $agentSample = $samples[$samples.Count - 1]
        }
    }

    return [pscustomobject]@{
        ComponentBProbe = $componentBProbe
        ComponentB = if ($componentBProbe.Ok) { $componentBProbe.Data } else { $null }
        RelayProbe = $relayProbe
        Relay = if ($relayProbe.Ok) { $relayProbe.Data } else { $null }
        Session = $session
        AgentProbe = $agentProbe
        Agent = $agentSample
    }
}

function Show-ComponentBSnapshot {
    param([AllowNull()]$Snapshot)

    if ($null -eq $Snapshot.ComponentB) {
        Add-Check 'FAIL' "Component B diagnostics unavailable: $($Snapshot.ComponentBProbe.Error)"
        return
    }

    $b = $Snapshot.ComponentB
    $ingest = Get-Number (Get-NestedValue $b @('counts', 'ingestConnections'))
    $subscribers = Get-Number (Get-NestedValue $b @('details', 'subscribers'))
    $accepted = Get-Number (Get-NestedValue $b @('counts', 'acceptedFrames'))
    $predictions = Get-Number (Get-NestedValue $b @('counts', 'predictions'))
    $invalid = Get-Number (Get-NestedValue $b @('counts', 'invalidFrames'))
    $errors = Get-Number (Get-NestedValue $b @('counts', 'processingErrors'))
    $modelReady = Get-NestedValue $b @('values', 'modelReady')
    $temperature = Get-NestedValue $b @('values', 'temperatureSource')
    $lastFrame = Get-NestedValue $b @('values', 'lastFrameAt')
    $lastPrediction = Get-NestedValue $b @('values', 'lastPredictionAt')

    Add-Line ('[DATA] Component B: ingest={0}; Unity subscribers={1}; model ready={2}; accepted={3}; predictions={4}; invalid={5}; processing errors={6}; temperature={7}; frame age={8}; prediction age={9}' -f
        $ingest, $subscribers, (Format-Bool $modelReady), $accepted, $predictions,
        $invalid, $errors, (Format-Value $temperature), (Format-Age $lastFrame),
        (Format-Age $lastPrediction))

    if ($ingest -gt 0) {
        Add-Check 'PASS' 'Phone has an active Component B /ingest connection.'
    }
    else {
        Add-Check 'FAIL' 'No active phone connection to Component B /ingest.'
    }
    if ($subscribers -gt 0) {
        Add-Check 'PASS' 'Unity has an active Component B /stream subscription.'
    }
    else {
        Add-Check 'FAIL' 'No active Unity subscriber on Component B /stream.'
    }
    if ($modelReady -eq $false) {
        Add-Check 'FAIL' 'Component B reports that its inference model is unavailable.'
    }
    elseif ($modelReady -eq $true) {
        Add-Check 'PASS' 'Component B inference model is loaded.'
    }
    else {
        Add-Check 'WARN' 'Component B has not reported model readiness yet.'
    }

    if ($null -ne $lastFrame) {
        $frameAge = [Math]::Max(0, (Get-EpochSeconds) - (Get-Number $lastFrame))
        if ($frameAge -le 90) {
            Add-Check 'PASS' ('Component B received a frame {0:0.0}s ago.' -f $frameAge)
        }
        else {
            Add-Check 'FAIL' ('Component B input is stale: last frame was {0:0.0}s ago.' -f $frameAge)
        }
    }
    else {
        Add-Check 'FAIL' 'Component B has not accepted any PPG frame since this process started.'
    }

    if ($null -ne $lastPrediction) {
        $predictionAge = [Math]::Max(0, (Get-EpochSeconds) - (Get-Number $lastPrediction))
        if ($predictionAge -le 90) {
            Add-Check 'PASS' ('Component B produced a prediction {0:0.0}s ago.' -f $predictionAge)
        }
        else {
            Add-Check 'FAIL' ('Component B predictions are stale: last prediction was {0:0.0}s ago.' -f $predictionAge)
        }
    }
    else {
        Add-Check 'WARN' 'No Component B prediction exists yet. A fresh stream normally needs about 45 seconds of valid beats.'
    }
}

function Show-RelaySnapshot {
    param([AllowNull()]$Snapshot)

    if ($null -eq $Snapshot.Relay) {
        Add-Check 'FAIL' "Session relay diagnostics unavailable: $($Snapshot.RelayProbe.Error)"
        return
    }

    $relay = $Snapshot.Relay
    $mobileCount = Get-Number (Get-NestedValue $relay @('details', 'mobileConnections'))
    $questCount = Get-Number (Get-NestedValue $relay @('details', 'questConnections'))
    Add-Line "[DATA] Relay: mobile connections=$mobileCount; Quest connections=$questCount"

    if ($mobileCount -gt 0) {
        Add-Check 'PASS' 'A mobile app is connected to the session relay.'
    }
    else {
        Add-Check 'FAIL' 'No mobile app is connected to the session relay.'
    }
    if ($questCount -gt 0) {
        Add-Check 'PASS' 'Unity/Quest is connected to the session relay.'
    }
    else {
        Add-Check 'FAIL' 'Unity/Quest is not connected to the session relay.'
    }

    $session = $Snapshot.Session
    if ($null -eq $session) {
        Add-Check 'FAIL' 'The relay has no session diagnostics to compare.'
        return
    }

    $mobile = Get-NestedValue $session @('mobile')
    $quest = Get-NestedValue $session @('quest')
    $mobileValues = Get-NestedValue $mobile @('values')
    $questValues = Get-NestedValue $quest @('values')

    Add-Line ('[DATA] Mobile: report age={0}; wearable={1}; telemetry age={2}; B state={3}; frames sent={4}; frames acknowledged={5}; raw buffered={6}' -f
        (Format-Age (Get-NestedValue $mobile @('receivedAt'))),
        (Format-Bool (Get-NestedValue $mobileValues @('wearableConnected'))),
        (Format-Value (Get-NestedValue $mobileValues @('telemetryAgeSeconds'))),
        (Format-Value (Get-NestedValue $mobileValues @('componentBState'))),
        (Format-Value (Get-NestedValue $mobileValues @('framesSent'))),
        (Format-Value (Get-NestedValue $mobileValues @('framesAccepted'))),
        (Format-Value (Get-NestedValue $mobileValues @('rawSamplesBuffered'))))

    Add-Line ('[DATA] Unity: report age={0}; phase={1}; B socket={2}; accepted windows={3}; visual forwarded={4}; window age={5}; visual policy={6}; audio policy={7}; audio updates={8}' -f
        (Format-Age (Get-NestedValue $quest @('receivedAt'))),
        (Format-Value (Get-NestedValue $questValues @('phase'))),
        (Format-Value (Get-NestedValue $questValues @('physiologySocket'))),
        (Format-Value (Get-NestedValue $questValues @('physiologyPayloads'))),
        (Format-Value (Get-NestedValue $questValues @('visualPhysiologyWindows'))),
        (Format-Value (Get-NestedValue $questValues @('physiologyAgeSeconds'))),
        (Format-Value (Get-NestedValue $questValues @('visualPolicy'))),
        (Format-Value (Get-NestedValue $questValues @('audioPolicy'))),
        (Format-Value (Get-NestedValue $questValues @('audioOnlineUpdates'))))

    $mobileAgeTimestamp = Get-NestedValue $mobile @('receivedAt')
    if ($null -ne $mobileAgeTimestamp -and ((Get-EpochSeconds) - (Get-Number $mobileAgeTimestamp)) -le 15) {
        Add-Check 'PASS' 'Mobile runtime diagnostics are fresh.'
    }
    else {
        Add-Check 'WARN' 'Mobile runtime diagnostics are missing or older than 15 seconds.'
    }
    $questAgeTimestamp = Get-NestedValue $quest @('receivedAt')
    if ($null -ne $questAgeTimestamp -and ((Get-EpochSeconds) - (Get-Number $questAgeTimestamp)) -le 15) {
        Add-Check 'PASS' 'Unity runtime diagnostics are fresh.'
    }
    else {
        Add-Check 'FAIL' 'Unity runtime diagnostics are missing or older than 15 seconds.'
    }

    $physiologyAge = Get-NestedValue $questValues @('physiologyAgeSeconds')
    if ($null -eq $physiologyAge -or (Get-Number $physiologyAge -1) -lt 0) {
        Add-Check 'FAIL' 'Unity has not accepted a Component B physiology window.'
    }
    elseif ((Get-Number $physiologyAge) -le 90) {
        Add-Check 'PASS' ('Unity physiology input is fresh: age {0:0.0}s.' -f (Get-Number $physiologyAge))
    }
    else {
        Add-Check 'FAIL' ('Unity physiology input is stale: age {0:0.0}s.' -f (Get-Number $physiologyAge))
    }
}

function Show-AgentSnapshot {
    param([AllowNull()]$Snapshot)

    if ($null -eq $Snapshot.AgentProbe) {
        Add-Check 'WARN' 'No relay session label was available for RL-agent diagnostics.'
        return
    }
    if (-not $Snapshot.AgentProbe.Ok) {
        Add-Check 'WARN' "RL-agent monitor endpoint failed: $($Snapshot.AgentProbe.Error)"
        return
    }

    $state = Get-NestedValue $Snapshot.AgentProbe.Data @('state')
    if ($null -eq $Snapshot.Agent) {
        Add-Check 'FAIL' "Unity has not published RL-agent snapshots; monitor state=$(Format-Value $state). Rebuild Unity with the current ProjectMonitorReporter."
        return
    }

    $agent = $Snapshot.Agent
    $coordination = Get-NestedValue $agent @('coordination')
    $visual = Get-NestedValue $agent @('visual')
    $audio = Get-NestedValue $agent @('audio')
    Add-Line ('[DATA] RL monitor: state={0}; sample age={1}; phase={2}' -f
        (Format-Value $state), (Format-Age (Get-NestedValue $agent @('receivedAt'))),
        (Format-Value (Get-NestedValue $agent @('phase'))))
    Add-Line ('[DATA] Coordinator: owner={0}; stage={1}; reason={2}; remaining={3}s; completed={4}; cancelled={5}' -f
        (Format-Value (Get-NestedValue $coordination @('owner'))),
        (Format-Value (Get-NestedValue $coordination @('stage'))),
        (Format-Value (Get-NestedValue $coordination @('reason'))),
        (Format-Value (Get-NestedValue $coordination @('remainingSeconds'))),
        (Format-Value (Get-NestedValue $coordination @('completedTurns'))),
        (Format-Value (Get-NestedValue $coordination @('cancelledTurns'))))

    foreach ($entry in @(
        [pscustomobject]@{ Name = 'Visual'; Value = $visual },
        [pscustomobject]@{ Name = 'Audio'; Value = $audio }
    )) {
        $value = $entry.Value
        $inputValue = Get-NestedValue $value @('input')
        $decision = Get-NestedValue $value @('decision')
        $reward = Get-NestedValue $value @('reward')
        Add-Line ('[DATA] {0} agent: available={1}; mode={2}; state={3}; transitioning={4}; updates={5}; input={6}/{7}; input age={8}s' -f
            $entry.Name,
            (Format-Bool (Get-NestedValue $value @('available'))),
            (Format-Value (Get-NestedValue $value @('mode'))),
            (Format-Value (Get-NestedValue $value @('state'))),
            (Format-Bool (Get-NestedValue $value @('transitioning'))),
            (Format-Value (Get-NestedValue $value @('updates'))),
            (Format-Bool (Get-NestedValue $inputValue @('available'))),
            (Format-Value (Get-NestedValue $inputValue @('source'))),
            (Format-Value (Get-NestedValue $inputValue @('ageSeconds'))))
        Add-Line ('[DATA] {0} variables: applied={1}; target={2}' -f
            $entry.Name,
            (Format-Vector (Get-NestedValue $value @('applied'))),
            (Format-Vector (Get-NestedValue $value @('target'))))
        Add-Line ('[DATA] {0} decision: available={1}; sequence={2}; proposed={3}; executed={4}; safety={5}; result={6}; reward={7}/{8}' -f
            $entry.Name,
            (Format-Bool (Get-NestedValue $decision @('available'))),
            (Format-Value (Get-NestedValue $decision @('sequence'))),
            (Format-Value (Get-NestedValue $decision @('proposed'))),
            (Format-Value (Get-NestedValue $decision @('executed'))),
            (Format-Value (Get-NestedValue $decision @('safety'))),
            (Format-Value (Get-NestedValue $decision @('result'))),
            (Format-Value (Get-NestedValue $reward @('status'))),
            (Format-Value (Get-NestedValue $reward @('total'))))
    }

    if ([string]$state -eq 'live') {
        Add-Check 'PASS' 'The relay is receiving current RL-agent diagnostics from Unity.'
    }
    else {
        Add-Check 'FAIL' "RL-agent diagnostics are not live; relay state=$(Format-Value $state)."
    }
}

function Get-SnapshotMetric {
    param(
        [AllowNull()]$Snapshot,
        [ValidateSet('Accepted', 'Predictions', 'MobileAccepted', 'UnityWindows', 'VisualForwarded', 'VisualUpdates', 'AudioUpdates', 'CompletedTurns')]
        [string]$Metric
    )

    switch ($Metric) {
        'Accepted' { return Get-Number (Get-NestedValue $Snapshot.ComponentB @('counts', 'acceptedFrames')) }
        'Predictions' { return Get-Number (Get-NestedValue $Snapshot.ComponentB @('counts', 'predictions')) }
        'MobileAccepted' { return Get-Number (Get-NestedValue $Snapshot.Session @('mobile', 'values', 'framesAccepted')) }
        'UnityWindows' { return Get-Number (Get-NestedValue $Snapshot.Session @('quest', 'values', 'physiologyPayloads')) }
        'VisualForwarded' { return Get-Number (Get-NestedValue $Snapshot.Session @('quest', 'values', 'visualPhysiologyWindows')) }
        'VisualUpdates' { return Get-Number (Get-NestedValue $Snapshot.Agent @('visual', 'updates')) }
        'AudioUpdates' { return Get-Number (Get-NestedValue $Snapshot.Agent @('audio', 'updates')) }
        'CompletedTurns' { return Get-Number (Get-NestedValue $Snapshot.Agent @('coordination', 'completedTurns')) }
    }
}

Add-Line '============================================================'
Add-Line 'MindSync Windows end-to-end diagnostics'
Add-Line '============================================================'
Add-Line ('Run time (UTC): {0}' -f ([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')))
Add-Line ('PowerShell: {0}' -f $PSVersionTable.PSVersion)
Add-Line 'Privacy: no keys, user/session identities, audio, or raw physiology are printed.'
Add-Line ''

Add-Line '--- Clock and LAN ---'
try {
    $timeService = Get-Service -Name W32Time -ErrorAction Stop
    Add-Line "[DATA] Windows Time service: $($timeService.Status)"
    if ($timeService.Status -ne 'Running') {
        Add-Check 'WARN' 'Windows Time is not running; incorrect clocks can make live windows appear stale.'
    }
}
catch {
    Add-Check 'WARN' 'Windows Time service status could not be read.'
}

$activeAddresses = New-Object 'System.Collections.Generic.List[string]'
try {
    $networkConfigurations = @(Get-NetIPConfiguration -ErrorAction Stop)
    foreach ($configuration in $networkConfigurations) {
        $adapterStatus = Get-NestedValue $configuration @('NetAdapter', 'Status')
        if ([string]$adapterStatus -ne 'Up') {
            continue
        }
        foreach ($address in @(Get-NestedValue $configuration @('IPv4Address') @())) {
            $ip = [string](Get-NestedValue $address @('IPAddress'))
            if ([string]::IsNullOrWhiteSpace($ip) -or $ip -eq '127.0.0.1' -or $ip.StartsWith('169.254.')) {
                continue
            }
            if (-not $activeAddresses.Contains($ip)) {
                [void]$activeAddresses.Add($ip)
            }
            $gateway = @((Get-NestedValue $configuration @('IPv4DefaultGateway') @()) | ForEach-Object {
                Get-NestedValue $_ @('NextHop')
            }) -join ','
            $profile = Format-Value (Get-NestedValue $configuration @('NetProfile', 'NetworkCategory'))
            Add-Line "[DATA] LAN: interface=$($configuration.InterfaceAlias); IPv4=$ip; gateway=$(Format-Value $gateway); profile=$profile"
        }
    }
}
catch {
    Add-Check 'FAIL' "Could not inspect Windows network adapters: $($_.Exception.Message)"
}

if ($activeAddresses.Count -gt 0) {
    Add-Check 'PASS' ('Active LAN IPv4 address(es): ' + ($activeAddresses -join ', '))
}
else {
    Add-Check 'FAIL' 'No active non-loopback IPv4 address was found.'
}

try {
    $profiles = @(Get-NetFirewallProfile -ErrorAction Stop)
    foreach ($profile in $profiles) {
        Add-Line "[DATA] Firewall: $($profile.Name); enabled=$($profile.Enabled); inbound=$($profile.DefaultInboundAction)"
    }
}
catch {
    Add-Check 'WARN' 'Windows Firewall profile status could not be read.'
}

Add-Line ''
Add-Line '--- Source endpoint configuration ---'
$configuredHosts = New-Object 'System.Collections.Generic.List[object]'
$mobileEnvironment = Join-Path $RepoRoot 'MindSync-VR\react-native-app\src\config\environment.ts'
$generatedMobileEnvironment = Join-Path $RepoRoot 'MindSync-VR\react-native-app\src\config\generatedEnvironment.ts'
$unityProfile = Join-Path $RepoRoot 'unity\Assets\Laminar VR\Configuration\Networking\LocalDevelopmentNetworkProfile.asset'

if (Test-Path -LiteralPath $mobileEnvironment) {
    $mobileText = Get-Content -LiteralPath $mobileEnvironment -Raw
    $mobileMatch = [regex]::Match($mobileText, 'developmentBackendHost\s*=\s*[''"]([^''"]+)[''"]')
    if ($mobileMatch.Success) {
        [void]$configuredHosts.Add([pscustomobject]@{ Name = 'Mobile source'; Host = $mobileMatch.Groups[1].Value })
    }
    if ($mobileText -match '\.invalid') {
        Add-Check 'WARN' 'Mobile release endpoints still contain .invalid placeholders. This does not block a debug build, but release APKs need real endpoints.'
    }
    if ($mobileText -match "componentDBaseUrl:\s*__DEV__\s*\?\s*'http://localhost:8010'") {
        Add-Check 'WARN' 'Mobile Component D development URL uses localhost. On a physical phone, localhost means the phone; this is separate from the RL physiology path.'
    }
}
else {
    Add-Check 'WARN' 'Mobile environment source file was not found under the supplied repository root.'
}

if (Test-Path -LiteralPath $generatedMobileEnvironment) {
    $generatedMobileText = Get-Content -LiteralPath $generatedMobileEnvironment -Raw
    $pilotHostMatch = [regex]::Match(
        $generatedMobileText,
        '[''"]?pilotBackendHost[''"]?\s*:\s*[''"]([^''"]*)[''"]'
    )
    if ($pilotHostMatch.Success -and -not [string]::IsNullOrWhiteSpace($pilotHostMatch.Groups[1].Value)) {
        [void]$configuredHosts.Add([pscustomobject]@{
            Name = 'Mobile generated release config'
            Host = $pilotHostMatch.Groups[1].Value
        })
        Add-Check 'PASS' 'PILOT_BACKEND_HOST is present in the generated mobile configuration.'
    }
    else {
        Add-Check 'WARN' 'PILOT_BACKEND_HOST is missing from the generated mobile configuration; standalone release builds will fail closed.'
    }
}
else {
    Add-Check 'WARN' 'Generated mobile configuration is missing. Run npm run prepare:env in react-native-app.'
}

if (Test-Path -LiteralPath $unityProfile) {
    $unityText = Get-Content -LiteralPath $unityProfile -Raw
    $unityMatch = [regex]::Match($unityText, '(?m)^\s*host:\s*(\S+)\s*$')
    if ($unityMatch.Success) {
        [void]$configuredHosts.Add([pscustomobject]@{ Name = 'Unity source'; Host = $unityMatch.Groups[1].Value })
    }
}
else {
    Add-Check 'WARN' 'Unity local network profile was not found under the supplied repository root.'
}

foreach ($configured in $configuredHosts) {
    Add-Line "[DATA] $($configured.Name) backend host=$($configured.Host)"
    if ($activeAddresses.Contains([string]$configured.Host)) {
        Add-Check 'PASS' "$($configured.Name) points to an active IPv4 address on this PC."
    }
    else {
        Add-Check 'WARN' "$($configured.Name) does not match this PC's active LAN IPv4 address. Installed APK/Quest builds may also contain an older baked-in host."
    }
}

Add-Line ''
Add-Line '--- Listeners and local health ---'
$services = @(
    [pscustomobject]@{ Name = 'Component B'; Port = 8000; Path = '/health'; Core = $true; RequireLan = $true },
    [pscustomobject]@{ Name = 'Component D'; Port = 8010; Path = '/health'; Core = $false; RequireLan = $true },
    [pscustomobject]@{ Name = 'Session relay'; Port = 8080; Path = '/health'; Core = $true; RequireLan = $true },
    [pscustomobject]@{ Name = 'Lyria backend'; Port = 8002; Path = '/health'; Core = $false; RequireLan = $true },
    [pscustomobject]@{ Name = 'Project monitor'; Port = 8090; Path = '/api/snapshot'; Core = $false; RequireLan = $false }
)

foreach ($service in $services) {
    $listeners = @(Get-TcpConnections -Port $service.Port -State 'Listen')
    if ($listeners.Count -eq 0) {
        Add-Check $(if ($service.Core) { 'FAIL' } else { 'WARN' }) "$($service.Name) is not listening on TCP $($service.Port)."
    }
    else {
        $descriptions = foreach ($listener in $listeners) {
            "$($listener.LocalAddress) ($(Get-ProcessLabel -ProcessId $listener.OwningProcess))"
        }
        Add-Line "[DATA] $($service.Name) listener: $($descriptions -join '; ')"
        $lanBound = @($listeners | Where-Object {
            $_.LocalAddress -in @('0.0.0.0', '::', '[::]') -or $activeAddresses.Contains([string]$_.LocalAddress)
        }).Count -gt 0
        if ($service.RequireLan -and -not $lanBound) {
            Add-Check $(if ($service.Core) { 'FAIL' } else { 'WARN' }) "$($service.Name) listens only on loopback; phone/Quest cannot reach it. Start Uvicorn with --host 0.0.0.0."
        }
        else {
            Add-Check 'PASS' "$($service.Name) listener binding is appropriate."
        }
    }

    $health = Invoke-JsonProbe -Uri "http://127.0.0.1:$($service.Port)$($service.Path)" -TimeoutSec 5
    if ($health.Ok) {
        Add-Check 'PASS' "$($service.Name) responds locally at $($service.Path)."
    }
    else {
        Add-Check $(if ($service.Core) { 'FAIL' } else { 'WARN' }) "$($service.Name) local probe failed: $($health.Error)"
    }
}

foreach ($port in @(8000, 8080)) {
    $connections = @(Get-TcpConnections -Port $port -State 'Established')
    Add-Line "[DATA] TCP $port established client connections=$($connections.Count)"
}

Add-Line ''
Add-Line '--- Runtime pipeline snapshot ---'
$first = Get-DiagnosticsSnapshot
Show-ComponentBSnapshot -Snapshot $first
Show-RelaySnapshot -Snapshot $first
Show-AgentSnapshot -Snapshot $first

if ($ObserveSeconds -gt 0 -and ($first.ComponentBProbe.Ok -or $first.RelayProbe.Ok)) {
    Add-Line ''
    Add-Line "--- Counter movement over $ObserveSeconds seconds ---"
    Add-Line '[INFO] Keep the wearable connected, mobile session active, and Unity in the running session during this observation.'
    Start-Sleep -Seconds $ObserveSeconds
    $second = Get-DiagnosticsSnapshot

    $acceptedDelta = (Get-SnapshotMetric $second 'Accepted') - (Get-SnapshotMetric $first 'Accepted')
    $predictionDelta = (Get-SnapshotMetric $second 'Predictions') - (Get-SnapshotMetric $first 'Predictions')
    $mobileAcceptedDelta = (Get-SnapshotMetric $second 'MobileAccepted') - (Get-SnapshotMetric $first 'MobileAccepted')
    $unityDelta = (Get-SnapshotMetric $second 'UnityWindows') - (Get-SnapshotMetric $first 'UnityWindows')
    $visualForwardedDelta = (Get-SnapshotMetric $second 'VisualForwarded') - (Get-SnapshotMetric $first 'VisualForwarded')
    $visualUpdatesDelta = (Get-SnapshotMetric $second 'VisualUpdates') - (Get-SnapshotMetric $first 'VisualUpdates')
    $audioUpdatesDelta = (Get-SnapshotMetric $second 'AudioUpdates') - (Get-SnapshotMetric $first 'AudioUpdates')
    $turnDelta = (Get-SnapshotMetric $second 'CompletedTurns') - (Get-SnapshotMetric $first 'CompletedTurns')

    Add-Line "[DATA] Deltas: B accepted=$acceptedDelta; mobile acknowledgements=$mobileAcceptedDelta; B predictions=$predictionDelta; Unity windows=$unityDelta; visual forwards=$visualForwardedDelta; visual updates=$visualUpdatesDelta; audio updates=$audioUpdatesDelta; coordinated turns=$turnDelta"

    if ($acceptedDelta -gt 0 -and $mobileAcceptedDelta -gt 0) {
        Add-Check 'PASS' 'Phone-to-Component-B frame delivery and acknowledgements are advancing.'
    }
    elseif ($acceptedDelta -gt 0) {
        Add-Check 'WARN' 'Component B accepted new frames, but the mobile acknowledgement counter did not advance in relay diagnostics.'
    }
    else {
        Add-Check 'FAIL' 'Component B accepted no new PPG frames during the observation period.'
    }

    if ($predictionDelta -gt 0) {
        Add-Check 'PASS' 'Component B predictions are advancing.'
    }
    elseif ((Get-SnapshotMetric $second 'Predictions') -eq 0 -and (Get-SnapshotMetric $second 'Accepted') -gt 0) {
        Add-Check 'WARN' 'Frames are arriving but no prediction exists yet. Continue for at least 45-60 seconds with a stable finger signal.'
    }
    else {
        Add-Check 'FAIL' 'Component B prediction count did not advance while the test was running.'
    }

    if ($predictionDelta -gt 0 -and $unityDelta -le 0) {
        Add-Check 'FAIL' 'Component B produced predictions, but Unity accepted no new physiology windows. Check Unity Component B host/build and /stream subscription.'
    }
    elseif ($unityDelta -gt 0) {
        Add-Check 'PASS' 'New Component B predictions reached Unity.'
    }
    else {
        Add-Check 'WARN' 'No new Unity physiology window was expected unless Component B produced a new prediction during this short observation.'
    }

    if ($visualForwardedDelta -eq 0) {
        Add-Check 'INFO' "No visual forward in $ObserveSeconds seconds can be normal; the visual bridge is rate-limited to roughly one window per minute."
    }
    if ($visualUpdatesDelta -eq 0 -and $audioUpdatesDelta -eq 0 -and $turnDelta -eq 0) {
        Add-Check 'INFO' 'No agent update in this short interval can be normal; inspect the coordinator owner/stage/reason above and observe through a full 75+ second decision period.'
    }

    Add-Line ''
    Add-Line '--- Latest state after observation ---'
    Show-ComponentBSnapshot -Snapshot $second
    Show-RelaySnapshot -Snapshot $second
    Show-AgentSnapshot -Snapshot $second
}

Add-Line ''
Add-Line '============================================================'
Add-Line "Summary: PASS=$script:PassCount WARN=$script:WarnCount FAIL=$script:FailCount"
if ($script:FailCount -eq 0) {
    Add-Line '[RESULT] No hard connectivity failure was found. Use coordinator reason/stage and the 75+ second schedule to explain delayed actions.'
}
else {
    Add-Line '[RESULT] One or more hard failures were found. Fix the earliest failing stage before testing RL actions again.'
}
Add-Line 'Send this complete output back for diagnosis. It intentionally excludes keys, participant/session identity, audio, and raw physiology.'
Add-Line '============================================================'

$script:Report
