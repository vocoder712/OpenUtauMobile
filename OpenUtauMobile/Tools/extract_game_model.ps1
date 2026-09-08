# ---------------------------------------------------------------------------
# 从 game.cpp release .oudep(zip) 中提取 game_medium.gguf 到 obj/。
# 供 OpenUtauMobile.csproj 的 EnsureGameEmbeddedModel 目标调用（构建期），
# 使 55MB 模型不 commit 进仓库，而是构建时"扒"自官方 release。
# 用法: powershell -ExecutionPolicy Bypass -File extract_game_model.ps1 -Oudep <zip> -Target <gguf>
# ---------------------------------------------------------------------------
param(
    [Parameter(Mandatory = $true)][string]$Oudep,
    [Parameter(Mandatory = $true)][string]$Target
)

$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $Target) {
    Write-Output "GAME 模型已存在: $Target"
    exit 0
}

if (-not (Test-Path -LiteralPath $Oudep)) {
    Write-Error "找不到 .oudep: $Oudep"
    exit 1
}

Write-Output "从 .oudep 提取 game_medium.gguf -> $Target"

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($Oudep)
try {
    $entry = $zip.GetEntry('game_medium.gguf')
    if ($null -eq $entry) {
        Write-Error ".oudep 中没有 game_medium.gguf 条目"
        exit 2
    }

    $dir = [System.IO.Path]::GetDirectoryName($Target)
    if (-not [string]::IsNullOrEmpty($dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $tmp = "$Target.tmp"
    $stream = $entry.Open()
    try {
        $file = [System.IO.File]::Create($tmp)
        try {
            $stream.CopyTo($file)
        } finally {
            $file.Dispose()
        }
    } finally {
        $stream.Dispose()
    }

    if (Test-Path -LiteralPath $Target) {
        Remove-Item -LiteralPath $Target -Force
    }

    Move-Item -LiteralPath $tmp -Destination $Target -Force | Out-Null
    Write-Output ("GAME 模型解压完成: {0} ({1:N0} B)" -f $Target, (Get-Item -LiteralPath $Target).Length)
    exit 0
} finally {
    $zip.Dispose()
}
