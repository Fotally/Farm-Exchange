param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$mapping = Get-Content -LiteralPath (Join-Path $repoRoot '.codex/rule-links.json') -Raw | ConvertFrom-Json
$errors = [Collections.Generic.List[string]]::new()

foreach ($entry in $mapping) {
    foreach ($document in @($entry.source) + @($entry.links)) {
        $documentPath = Join-Path $repoRoot $document
        if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) {
            $errors.Add("规则入口不存在：$document -> $documentPath")
            continue
        }
        $content = Get-Content -LiteralPath $documentPath -Raw
        $references = @(
            foreach ($match in [regex]::Matches($content, '\]\(([^)]+)\)')) {
                $destination = $match.Groups[1].Value.Split('#')[0]
                if ($destination -eq '' -or $destination -match '^[a-z][a-z0-9+.-]*:' -or $destination.StartsWith('//')) { continue }
                @{ Reference = $destination; Base = Split-Path -Parent $documentPath }
            }
            foreach ($match in [regex]::Matches($content, '仓库根路径：`([^`]+)`')) {
                @{ Reference = $match.Groups[1].Value; Base = $repoRoot }
            }
        )
        foreach ($reference in $references) {
            $destination = [uri]::UnescapeDataString($reference.Reference)
            $resolved = [IO.Path]::GetFullPath((Join-Path $reference.Base $destination))
            if (-not (Test-Path -LiteralPath $resolved)) {
                $errors.Add("规则引用目标不存在：入口 $document；引用 $($reference.Reference)；解析目标 $resolved")
            }
        }
    }
}

if ($errors.Count -gt 0) {
    throw ($errors -join [Environment]::NewLine)
}
Write-Host '规则原文与目录入口的内部引用检查通过'
