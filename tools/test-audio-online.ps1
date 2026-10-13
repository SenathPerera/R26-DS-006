param(
    [string]$UnityEditorData = 'C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Data'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root '.codex_tmp/audio-online-verification'
[IO.Directory]::CreateDirectory($output) | Out-Null
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$mono = Join-Path $UnityEditorData 'MonoBleedingEdge/bin/mono.exe'
if (!(Test-Path $compiler) -or !(Test-Path $mono)) { throw 'Pass -UnityEditorData for an installed Unity editor.' }

# Compile every audio runtime/editor source against the installed Unity API and
# rebuilt shared public APIs. Do not open or upgrade the Unity project.
$references = @((Join-Path $UnityEditorData 'NetStandard/ref/2.1.0/netstandard.dll'))
$references += Get-ChildItem (Join-Path $UnityEditorData 'Managed') -Filter '*.dll' -Recurse | ForEach-Object FullName
foreach ($name in @('UnityEngine.UI', 'Unity.InputSystem', 'Unity.RenderPipelines.Core.Runtime', 'Unity.RenderPipelines.Core.Runtime.Shared', 'Unity.RenderPipelines.Universal.Runtime')) {
    $path = Join-Path $root "unity/Library/ScriptAssemblies/$name.dll"
    if (!(Test-Path $path)) { throw "Missing $path. Import the Unity project once before running the API compile check." }
    $references += $path
}
$baseArgs = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:9', '/target:library', '/define:UNITY_EDITOR')
$referenceArgs = $references | ForEach-Object { "/r:$_" }
$coreSources = Get-ChildItem (Join-Path $root 'unity/Assets/Laminar VR/Scripts/Core') -Filter '*.cs' -Recurse | ForEach-Object FullName
& $dotnet $compiler @baseArgs @referenceArgs "/out:$output/LaminarVR.AdaptiveMeditation.Core.dll" @coreSources
if ($LASTEXITCODE -ne 0) { throw 'Shared core reference build failed.' }
$references += "$output/LaminarVR.AdaptiveMeditation.Core.dll"
$referenceArgs = $references | ForEach-Object { "/r:$_" }
$runtimeSources = Get-ChildItem (Join-Path $root 'unity/Assets/Laminar VR/Scripts/Runtime') -Filter '*.cs' -Recurse | ForEach-Object FullName
& $dotnet $compiler @baseArgs @referenceArgs "/out:$output/LaminarVR.AdaptiveMeditation.Runtime.dll" @runtimeSources
if ($LASTEXITCODE -ne 0) { throw 'Shared runtime reference build failed.' }
$references += "$output/LaminarVR.AdaptiveMeditation.Runtime.dll"
$sources = Get-ChildItem (Join-Path $root 'unity/Assets/AdaptiveAudioVR') -Filter '*.cs' -Recurse | ForEach-Object FullName
$compilerArgs = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:9', '/target:library', '/define:UNITY_EDITOR', "/out:$output/AdaptiveAudioVerification.dll")
$compilerArgs += $references | ForEach-Object { "/r:$_" }
$compilerArgs += $sources
& $dotnet $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Audio Unity API compilation failed.' }
Write-Output 'PASS All audio runtime and editor sources compile against Unity API references.'

$testSources = Get-ChildItem (Join-Path $root 'tests/audio_online') -Filter '*.cs' | ForEach-Object FullName
$agentSources = Get-ChildItem (Join-Path $root 'unity/Assets/AdaptiveAudioVR/Scripts/RL/Agent') -Filter '*.cs' |
    Where-Object { $_.Name -notlike 'Ppo*' } | ForEach-Object FullName
$testSources += $agentSources
foreach ($name in @('AudioParameters', 'SignalPacket', 'AudioProfile', 'PersonalizationStrategy')) {
    $testSources += Join-Path $root "unity/Assets/AdaptiveAudioVR/Scripts/Core/$name.cs"
}
$testSources += Join-Path $root 'unity/Assets/AdaptiveAudioVR/Scripts/Core/UserPreferences.cs'
$testSources += Join-Path $root 'unity/Assets/AdaptiveAudioVR/Scripts/Preference/AudioSessionPreferenceMapper.cs'
$framework = Join-Path $UnityEditorData 'MonoBleedingEdge/lib/mono/4.5'
$testArgs = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:9', '/target:exe', "/out:$output/AudioOnlineTests.exe")
foreach ($name in @('mscorlib', 'System', 'System.Core', 'System.Web.Extensions')) { $testArgs += "/r:$framework/$name.dll" }
$testArgs += "/r:$framework/Facades/netstandard.dll"
$testArgs += "/r:$output/LaminarVR.AdaptiveMeditation.Core.dll"
$testArgs += $testSources
& $dotnet $compiler @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Audio standalone logic test compilation failed.' }
$testData = Join-Path $output ([Guid]::NewGuid().ToString('N'))
& $mono "$output/AudioOnlineTests.exe" $testData
if ($LASTEXITCODE -ne 0) { throw 'Audio online-learning verification failed.' }
Write-Output "Test output: $output"
