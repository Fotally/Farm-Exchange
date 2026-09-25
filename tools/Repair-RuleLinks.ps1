$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$mapping = Get-Content -LiteralPath (Join-Path $repoRoot '.codex/rule-links.json') -Raw | ConvertFrom-Json

foreach ($entry in $mapping) {
    $source = [IO.Path]::GetFullPath((Join-Path $repoRoot $entry.source))
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "规则原文不存在：$($entry.source)"
    }

    foreach ($link in $entry.links) {
        $linkPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $link))
        $linkDirectory = Split-Path -Parent $linkPath
        New-Item -ItemType Directory -Path $linkDirectory -Force | Out-Null
        $relativeTarget = [IO.Path]::GetRelativePath($linkDirectory, $source).Replace('\', '/')
        $existing = Get-Item -LiteralPath $linkPath -Force -ErrorAction SilentlyContinue

        if ($null -ne $existing) {
            if ($existing.LinkType -eq 'SymbolicLink') {
                $resolved = [IO.Path]::GetFullPath((Join-Path $linkDirectory $existing.Target))
                if ($resolved -eq $source) {
                    continue
                }
            }
            elseif (-not $existing.PSIsContainer -and
                (Get-Content -LiteralPath $linkPath -Raw).Trim() -eq $relativeTarget) {
                # Git 在 core.symlinks=false 时会将目标路径检出为普通文本。
            }
            else {
                throw "已有非预期文件，未覆盖：$link"
            }
            Remove-Item -LiteralPath $linkPath
        }

        New-Item -ItemType SymbolicLink -Path $linkPath -Target $relativeTarget | Out-Null
        Write-Host "已连接 $link -> $relativeTarget"
    }
}
