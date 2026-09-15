param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')]
    [string]$Name,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../../../..'),
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$allowed = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/ui-probes'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $allowed $Name }
$target = [IO.Path]::GetFullPath($OutputDirectory)
$comparison = [StringComparison]::OrdinalIgnoreCase
if (!$target.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw 'OUTPUT_OUTSIDE_PROBE_ROOT'
}
# 生成前检查所有已有祖先，避免链接目录把写入重定向到边界外。
$ancestor = $target
while ($ancestor) {
    if (Test-Path -LiteralPath $ancestor) {
        if ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "LINKED_OUTPUT_ANCESTOR: $ancestor"
        }
    }
    $ancestor = Split-Path -Parent $ancestor
}
if (Test-Path -LiteralPath $target) { throw 'OUTPUT_ALREADY_EXISTS' }
$project = Join-Path $repo 'OpenUtauMobile/OpenUtauMobile.csproj'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$raw = & dotnet msbuild $project -nologo -getProperty:TargetFramework,AvaloniaVersion
if ($LASTEXITCODE -ne 0) { throw 'PROJECT_PROPERTY_QUERY_FAILED' }
$properties = ($raw -join "`n" | ConvertFrom-Json).Properties
if (!$properties.TargetFramework -or !$properties.AvaloniaVersion) { throw 'PROJECT_PROPERTIES_MISSING' }
$template = Join-Path $PSScriptRoot '../assets/ui-probe'
$xml = [IO.File]::ReadAllText((Join-Path $template 'Probe.csproj.template'))
$relativeProject = [IO.Path]::GetRelativePath($target, $project)
$xml = $xml.Replace('__PROJECT__', [Security.SecurityElement]::Escape($relativeProject))
$xml = $xml.Replace('__FRAMEWORK__', [Security.SecurityElement]::Escape($properties.TargetFramework))
$xml = $xml.Replace('__AVALONIA__', [Security.SecurityElement]::Escape($properties.AvaloniaVersion))
New-Item -ItemType Directory -Path $target | Out-Null
# 项目边界固定，不从应用目录收集业务源码，也不加入应用解决方案。
[IO.File]::WriteAllText((Join-Path $target 'Probe.csproj'), $xml)
Get-ChildItem -LiteralPath $template -Filter '*.cs' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name)
}
Write-Output "GENERATED: $target"
Write-Output "FRAMEWORK: $($properties.TargetFramework); AVALONIA: $($properties.AvaloniaVersion)"
Write-Output "RUN: dotnet run --project `"$(Join-Path $target 'Probe.csproj')`" -- basic"
Write-Output 'SCENARIOS: basic, magnifier, settings (one scenario per process)'
