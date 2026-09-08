param([string]$Baseline = 'ca04e704ec091296073f95789d69b2fe8e6d24f0')
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
$artifacts = Join-Path $PWD 'artifacts/pr287'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$managerPath = 'OpenUtau.Core/DiffSinger/DiffSingerSpeakerEmbedManager.cs'
$baselineLines = git show "${Baseline}:$managerPath"
if ($LASTEXITCODE -ne 0) { throw 'Baseline source lookup failed.' }
$baselineSource = $baselineLines -join "`n"
$baselineFile = Join-Path $artifacts 'BaselineSpeakerEmbedManager.cs'
$baselineSource.Replace('DiffSingerSpeakerEmbedManager', 'BaselineSpeakerEmbedManager') | Set-Content $baselineFile
$modifiedSource = [IO.File]::ReadAllText((Join-Path $PWD $managerPath)).Replace("`r`n", "`n")
$startMarker = '//get default speaker for each padded segment'
$baselineStart = $baselineSource.IndexOf($startMarker)
$modifiedStart = $modifiedSource.IndexOf($startMarker)
$baselineEnd = $baselineSource.IndexOf('            var spkEmbedResult = np.dot', $baselineStart)
$modifiedEnd = $modifiedSource.IndexOf('            float[][] embeddings', $modifiedStart)
if ($baselineStart -lt 0 -or $modifiedStart -lt 0 -or $baselineEnd -lt 0 -or $modifiedEnd -lt 0) { throw 'Frame implementation markers missing.' }
if ($baselineSource.Substring($baselineStart, $baselineEnd - $baselineStart) -cne $modifiedSource.Substring($modifiedStart, $modifiedEnd - $modifiedStart)) { throw 'Upstream frame mapping or weight logic changed.' }
if ($modifiedSource.Contains('np.dot')) { throw 'Speaker manager still calls np.dot.' }
foreach ($path in @('OpenUtau.Core/DiffSinger/Phonemizers/DiffSingerG2pPhonemizer.cs', 'OpenUtauMobile/Helpers/LocalizationManager.cs')) {
    $expected = git rev-parse "${Baseline}:$path"
    $actual = git hash-object -- $path
    if ($LASTEXITCODE -ne 0 -or $expected -ne $actual) { throw "Unexpected diff: $path" }
}
"BASELINE_COMMIT=$Baseline"
"BASELINE_MANAGER_BLOB=$(git rev-parse "${Baseline}:$managerPath")"
'PASS upstream frame mapping and weight logic byte-for-byte (normalized line endings)'
'PASS G2P and Localization Git blobs unchanged'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet run --project tests/DiffSingerCompatibility -p:BaselineSource="$baselineFile"
exit $LASTEXITCODE
