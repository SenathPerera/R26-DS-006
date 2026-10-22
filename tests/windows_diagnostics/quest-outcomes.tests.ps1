$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $root 'tools/inspect-mindsync-quest-rl.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors.Message -join '; ') }
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$row = [pscustomobject]@{
    eventType = 'outcome'; createdUtc = '2026-10-18T12:11:38Z'; rewardValid = $false;
    onlineUpdateApplied = $false; executedArm = 0;
    outcomeReason = 'Realtime playback gap or dropped PCM; outcome discarded';
    sessionId = 'private-session'; userId = 'private-participant'; state = @{ rawPhysiology = 'private-signal' }
}
$result = Get-PublicOutcome $row
Assert ($result.category -eq 'Audio playback/content change') 'Playback cancellation category missing.'
Assert (-not $result.valid -and -not $result.learned) 'Invalid outcome presented as training.'
$json = $result | ConvertTo-Json -Compress
Assert ($json -notmatch 'private-session|private-participant|private-signal|userId|sessionId|state') 'Private log data leaked.'
$row.outcomeReason = 'Malformed, stale, or future physiology window.'
Assert ((Get-PublicOutcome $row).category -eq 'Input timing/format') 'Timing cancellation category missing.'
$row.outcomeReason = 'Invalid or unreliable stress/confidence/quality.'
Assert ((Get-PublicOutcome $row).category -eq 'Input reliability') 'Quality cancellation category missing.'
$row.outcomeReason = 'Failed request https://example.invalid/path?key=secret 12345678-1234-1234-1234-123456789abc'
Assert ((Get-PublicOutcome $row).reason -notmatch 'secret|12345678') 'URL or opaque ID leaked.'
$row.eventType = 'decision'
Assert ($null -eq (Get-PublicOutcome $row)) 'Pending decision treated as a completed outcome.'
$row.eventType = 'outcome'; $row.rewardValid = 'false'
Assert ($null -eq (Get-PublicOutcome $row)) 'Malformed string boolean presented as a valid reward.'
$row.rewardValid = $true; $row.onlineUpdateApplied = $true
$row.outcomeReason = 'Accepted for private-session and private-participant'
$accepted = Get-PublicOutcome $row
Assert ($accepted.valid -and $accepted.learned -and $accepted.category -eq 'Accepted outcome') 'Valid learning outcome was not preserved.'
Assert ($accepted.reason -notmatch 'private-session|private-participant') 'Identity embedded in an outcome reason leaked.'
Assert ($null -eq (Get-PublicOutcome ([pscustomobject]@{ eventType = 'outcome' }))) 'Incomplete log record was not skipped.'
$row.executedArm = 'malformed-private-arm'
Assert ($null -eq (Get-PublicOutcome $row)) 'Invalid action value was not skipped.'
Assert ((Get-SessionLabel 'abc') -eq 'ba7816bf8f') 'Session label does not match relay SHA256 label.'
$sdk = Join-Path ([IO.Path]::GetTempPath()) ('mindsync-adb-test-' + [guid]::NewGuid().ToString('N'))
$originalAndroidHome = $env:ANDROID_HOME
try {
    $platformTools = New-Item -ItemType Directory -Path (Join-Path $sdk 'platform-tools') -Force
    $fakeAdb = New-Item -ItemType File -Path (Join-Path $platformTools.FullName 'adb.exe')
    $env:ANDROID_HOME = $sdk
    $AdbPath = ''
    function Get-Command { param($Name, $ErrorAction) return $null }
    Assert ((Find-Adb) -eq $fakeAdb.FullName) 'ADB SDK fallback lookup failed.'
    Remove-Item Function:Get-Command
    $AdbPath = $fakeAdb.FullName
    Assert ((Find-Adb) -eq $fakeAdb.FullName) 'Explicit ADB path lookup failed.'
}
finally {
    Remove-Item Function:Get-Command -ErrorAction SilentlyContinue
    $env:ANDROID_HOME = $originalAndroidHome
    Remove-Item -LiteralPath $sdk -Recurse -Force -ErrorAction SilentlyContinue
}

$fixtureRecord = [pscustomobject]@{
    schemaVersion = 2; eventType = 'outcome'; sessionId = 'abc'; userId = 'private-participant';
    createdUtc = '2026-10-18T12:11:38Z'; rewardValid = $false; onlineUpdateApplied = $false;
    executedArm = 0; outcomeReason = 'Realtime playback gap or dropped PCM; outcome discarded'
} | ConvertTo-Json -Compress
$adbCalls = New-Object 'System.Collections.Generic.List[string]'
function Invoke-FakeAdb {
    $global:LASTEXITCODE = 0
    $command = $args -join ' '
    [void]$adbCalls.Add($command)
    switch -Regex ($command) {
        '^devices$' { return @('List of devices attached', "private-device`tdevice") }
        ' getprop ro.product.model$' { return 'Quest 3' }
        ' ls -t ' { return "/sdcard/Android/data/com.mindsyncvr.templepond/files/RLTransitions/audio_rl_transitions_abc.jsonl`r" }
        ' tail -n 500 ' { return @('incomplete-json-line', $fixtureRecord) }
        default { throw 'Unexpected ADB command in read-only inspection.' }
    }
}
function Get-Command { param($Name, $ErrorAction) return [pscustomobject]@{ Source = 'Invoke-FakeAdb' } }
function Invoke-RestMethod {
    param($Uri, $Method, $TimeoutSec)
    if ($Uri -eq 'http://127.0.0.1:8080/monitor/status') {
        return [pscustomobject]@{ details = @{ sessions = @(@{ label = 'ba7816bf8f'; questConnected = $true }) } }
    }
    if ($Uri -like 'http://127.0.0.1:8080/monitor/agents*') {
        Assert ($Uri -eq 'http://127.0.0.1:8080/monitor/agents?session=ba7816bf8f') 'Relay query did not match the Quest log session.'
        $now = ([DateTimeOffset]::UtcNow - [DateTimeOffset]'1970-01-01T00:00:00Z').TotalSeconds
        return [pscustomobject]@{
            state = 'live'; samples = @(@{
                receivedAt = $now; phase = 'Adaptive';
                coordination = @{ owner = 'Audio'; stage = 'Measuring'; reason = 'Test coordinator reason'; completedTurns = 0; cancelledTurns = 1 }
            })
        }
    }
    return [pscustomobject]@{}
}
try {
    $report = @(& $source) -join "`n"
    Assert ($report -match '\[OUTCOME\].*valid=False.*Audio playback/content change') 'Full Quest outcome inspection failed.'
    Assert ($report -match 'owner=Audio; stage=Measuring; reason=Test coordinator reason') 'Same-session coordinator report missing.'
    Assert ($report -match '\[PASS\] The relay is receiving current RL-agent diagnostics') 'Live relay diagnostics were not inspected.'
    Assert ($report -notmatch 'private-device|private-participant|transitions_abc|ba7816bf8f|incomplete-json-line') 'Full report exposed private or raw log data.'
    Assert ($adbCalls.Count -eq 4) 'Unexpected ADB command count.'
}
finally {
    Remove-Item Function:Get-Command, Function:Invoke-RestMethod, Function:Invoke-FakeAdb
}
Write-Output 'PASS Quest inspection syntax, categories, redaction, ADB discovery and full mocked Quest-to-relay inspection.'
