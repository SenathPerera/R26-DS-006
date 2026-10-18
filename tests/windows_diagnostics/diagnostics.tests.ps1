# Standalone diagnostic checks; no live services or Windows modules required.
[CmdletBinding()]
param([string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = Join-Path $RepoRoot 'tools/diagnose-mindsync-windows.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors.Message -join '; ') }
foreach ($definition in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$script:Report = New-Object 'System.Collections.Generic.List[string]'
$script:PassCount = $script:WarnCount = $script:FailCount = 0
Show-RLConfiguration
Assert-True ($script:AudioMinimumQuality -gt 0) 'Audio quality limit was not read.'
Assert-True ($script:VisualForwardInterval -gt 0) 'Visual forwarding interval was not read.'
Assert-True ($null -eq (Get-SourceNumber 'nonexistent.fixture' 'anything')) 'Missing source should remain unknown.'

$epoch = Get-EpochSeconds
$decision = @{ available = $true; sequence = 1; proposed = 'Hold stable'; executed = 'Hold stable'; safety = 'Normal'; result = 'Selected' }
$invalidReward = @{ available = $true; valid = $false; status = 'Invalid'; total = 0 }
$inputValue = @{ available = $true; source = 'ComponentB'; stress = 0.303; confidence = 0.592; quality = 0.58; ageSeconds = 15 }
$audio = @{ available = $true; mode = 'PersonalizedBandit'; state = 'SignalQualityFreeze'; transitioning = $false;
    updates = 0; applied = @(0.13, 0.072); target = @(0.13, 0.072); input = $inputValue; decision = $decision; reward = $invalidReward }
$script:AgentFixture = @{ receivedAt = $epoch; phase = 'Adaptive'; audio = $audio;
    coordination = @{ owner = 'None'; stage = 'Waiting'; reason = 'Invalid or unreliable stress/confidence/quality.';
        remainingSeconds = 0; completedTurns = 0; cancelledTurns = 2; eligiblePostStartUtc = $epoch - 10 } }
$script:PredictionFixture = @{ windowStart = $epoch - 75; windowEnd = $epoch; signalQuality = 0.58;
    stress = @{ confidence = 0.592; mode = 'point' } }
$snapshot = [pscustomobject]@{ Agent = $script:AgentFixture; AgentProbe = @{ Ok = $true; Data = @{ state = 'live' } }; Prediction = $script:PredictionFixture }

$script:Report.Clear()
Show-AgentSnapshot $snapshot
Show-PredictionSnapshot $snapshot
Add-AgentTrace $snapshot 10
$reportText = $script:Report -join "`n"
Assert-True ($reportText -match 'quality=0.58') 'Quality was missing from the agent report.'
Assert-True ($reportText -match 'input reliability gate failed') 'Runtime freeze was not identified.'
Assert-True ($reportText -match 'below the audio source minimum') 'Low quality did not trigger a source-limit warning.'
Assert-True ($reportText -match 'last reward was invalid') 'Invalid reward was not identified.'
Assert-True ($reportText -match 'reward=Invalid/not attributed') 'Invalid reward was presented as a numeric reward.'
Assert-True ($reportText -match 'may arrive after the reward deadline') 'Visual timing risk was missing.'
Assert-True ($reportText -match '\[TRACE \+10s\].*cancelled=2') 'Coordinator trace was missing.'
Assert-True ($reportText -match 'still overlaps transition/settling') 'Post-window overlap was missing.'

$script:Report.Clear()
$inputValue.quality = 0.9
$audio.state = 'Normal'
$audio.reward = @{ available = $true; valid = $true; status = 'Updated'; total = 0.2 }
$audio.updates = 1
$script:PredictionFixture.signalQuality = 0.9
$script:PredictionFixture.stress.confidence = 0.9
$script:PredictionFixture.windowStart = $epoch - 40
Show-AgentSnapshot $snapshot
Show-PredictionSnapshot $snapshot
$reportText = $script:Report -join "`n"
Assert-True ($reportText -notmatch '\[WARN\]') 'Healthy fixture incorrectly warned about blocked learning.'
Assert-True ($reportText -match 'reward=Updated/0.2') 'Valid reward was missing.'

$script:Report.Clear()
$script:PredictionFixture.stress.confidence = 0.2
Show-PredictionSnapshot $snapshot
Assert-True (($script:Report -join "`n") -match 'confidence is below') 'Low confidence was not identified.'

$script:Report.Clear()
$empty = [pscustomobject]@{ Agent = $null; AgentProbe = $null; Prediction = $null }
Show-AgentSnapshot $empty
Show-PredictionSnapshot $empty
Add-AgentTrace $empty 20
Assert-True (($script:Report -join "`n") -match 'No agent snapshot') 'Missing snapshots were not handled.'

# The active session may change in the relay while a measurement is observed.
$script:ProbeUris = New-Object 'System.Collections.Generic.List[string]'
function Invoke-JsonProbe {
    param([string]$Uri, [int]$TimeoutSec = 4)
    [void]$script:ProbeUris.Add($Uri)
    $data = if ($Uri -like '*/monitor/agents*') {
        @{ state = 'live'; samples = @($script:AgentFixture) }
    } elseif ($Uri -like '*:8080/monitor/status') {
        @{ details = @{ sessions = @(
            @{ label = 'new-session'; mobileConnected = $true; questConnected = $true },
            @{ label = 'observed-session'; mobileConnected = $true; questConnected = $true }
        ) } }
    } elseif ($Uri -like '*/stress/latest') { $script:PredictionFixture } else { @{} }
    return [pscustomobject]@{ Ok = $true; Data = $data; Error = '' }
}
$selected = Get-DiagnosticsSnapshot -SessionLabel 'observed-session'
Assert-True ($selected.Session.label -eq 'observed-session') 'Observation switched sessions.'
Assert-True ($script:ProbeUris.Contains('http://127.0.0.1:8080/monitor/agents?session=observed-session')) 'Wrong agent endpoint selected.'
Assert-True ((Get-SnapshotMetric $selected 'CancelledTurns') -eq 2) 'Cancelled-turn counter was missing.'
$missing = Get-DiagnosticsSnapshot -SessionLabel 'session-no-longer-present'
Assert-True ($null -eq $missing.Session -and $null -eq $missing.Agent) 'Ended session was replaced with another session.'

# Exercise the entire timed script with read-only network fixtures.
function Get-Service { return [pscustomobject]@{ Status = 'Running' } }
function Get-NetIPConfiguration {
    return [pscustomobject]@{ InterfaceAlias = 'Fixture'; NetAdapter = @{ Status = 'Up' };
        IPv4Address = @(@{ IPAddress = '192.168.43.254' }); IPv4DefaultGateway = @(@{ NextHop = '192.168.43.1' });
        NetProfile = @{ NetworkCategory = 'Private' } }
}
function Get-NetFirewallProfile { return @() }
function Get-NetTCPConnection { return @() }
$fixtureState = @{ Ticks = 0; Agent = $script:AgentFixture; Prediction = $script:PredictionFixture }
function Invoke-RestMethod {
    param([string]$Uri, [string]$Method, [int]$TimeoutSec)
    if ($Uri -like '*:8000/monitor/status') {
        $fixtureState.Ticks++
        return @{ counts = @{ ingestConnections = 1; acceptedFrames = $fixtureState.Ticks; predictions = $fixtureState.Ticks };
            details = @{ subscribers = 1 }; values = @{ modelReady = $true; lastFrameAt = Get-EpochSeconds; lastPredictionAt = Get-EpochSeconds } }
    }
    if ($Uri -like '*/stress/latest') { return $fixtureState.Prediction }
    if ($Uri -like '*:8080/monitor/status') {
        $session = @{ label = 'observed-session'; mobileConnected = $true; questConnected = $true;
            mobile = @{ receivedAt = Get-EpochSeconds; values = @{ framesAccepted = $fixtureState.Ticks } };
            quest = @{ receivedAt = Get-EpochSeconds; values = @{ physiologyPayloads = $fixtureState.Ticks; physiologyAgeSeconds = 1 } } }
        return @{ details = @{ sessions = @($session); mobileConnections = 1; questConnections = 1 } }
    }
    if ($Uri -like '*/monitor/agents*') {
        $fixtureState.Agent.coordination.cancelledTurns = $fixtureState.Ticks
        return (@{ state = 'live'; samples = @($fixtureState.Agent) } | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
    }
    return @{ status = 'ok' }
}
$fullReport = (& $source -ObserveSeconds 2 -SampleIntervalSeconds 1 -RepoRoot $RepoRoot) -join "`n"
Assert-True ($fixtureState.Ticks -ge 3) 'Timed observation did not poll repeatedly.'
Assert-True ($fullReport -match 'Coordinated measurements were cancelled') 'Cancellation delta was not reported.'
Assert-True ($fullReport -match 'Deltas: B accepted=2;') 'Counter deltas were not calculated from the start/end samples.'

Write-Output 'PASS: PowerShell syntax, quality/confidence gates, rewards, timing, trace, missing data, session isolation and timed capture.'
