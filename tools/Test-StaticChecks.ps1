$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$mapping = Get-Content -LiteralPath (Join-Path $repoRoot '.codex/rule-links.json') -Raw | ConvertFrom-Json
$errors = [Collections.Generic.List[string]]::new()

foreach ($entry in $mapping) {
    $source = [IO.Path]::GetFullPath((Join-Path $repoRoot $entry.source))
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        $errors.Add("规则原文不存在：$($entry.source)")
    }
    foreach ($link in $entry.links) {
        $tracked = git -C $repoRoot ls-files --stage -- $link
        if ($tracked -notmatch '^120000\s') {
            $errors.Add("Git 中未按符号链接记录：$link")
        }
        $linkPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $link))
        $item = Get-Item -LiteralPath $linkPath -Force -ErrorAction SilentlyContinue
        if ($null -eq $item -or $item.LinkType -ne 'SymbolicLink') {
            $errors.Add("规则链接不存在或不是符号链接：$link")
            continue
        }
        $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $linkPath) $item.Target))
        if ($resolved -ne $source) {
            $errors.Add("规则链接目标错误：$link")
        }
    }
}

$documents = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs') -Recurse -File -Filter '*.md'
foreach ($document in $documents) {
    $relative = [IO.Path]::GetRelativePath((Join-Path $repoRoot 'docs'), $document.FullName).Replace('\', '/')
    if ($relative -ne 'README.md' -and $relative -notmatch '^(?:[a-z0-9]+(?:-[a-z0-9]+)*/)*[a-z0-9]+(?:-[a-z0-9]+)*\.md$') {
        $errors.Add("文档路径不符合小写连字符命名：docs/$relative")
    }

    $content = Get-Content -LiteralPath $document.FullName -Raw
    foreach ($match in [regex]::Matches($content, '\]\(([^)]+)\)')) {
        $destination = $match.Groups[1].Value.Split('#')[0]
        if ($destination -eq '' -or $destination -match '^[a-z][a-z0-9+.-]*:' -or $destination.StartsWith('//')) {
            continue
        }
        $localPath = [uri]::UnescapeDataString($destination)
        if (-not (Test-Path -LiteralPath (Join-Path $document.DirectoryName $localPath))) {
            $errors.Add("文档链接目标不存在：docs/$relative -> $destination")
        }
    }
}

$namespaces = @{ gameplay = 'FarmExchange.Gameplay'; market = 'FarmExchange.Market'; world = 'FarmExchange.World'; ui = 'FarmExchange.UI' }
foreach ($group in $namespaces.Keys) {
    $directory = Join-Path $repoRoot "scripts/$group"
    foreach ($script in Get-ChildItem -LiteralPath $directory -File -Filter '*.cs') {
        $content = Get-Content -LiteralPath $script.FullName -Raw
        if ($content -notmatch "(?m)^namespace $([regex]::Escape($namespaces[$group]));\r?$") {
            $errors.Add("脚本命名空间与路径不一致：scripts/$group/$($script.Name)")
        }
        if ($script.BaseName -cnotmatch '^[A-Z][A-Za-z0-9]*$') {
            $errors.Add("C# 文件名须与 PascalCase 类型一致：scripts/$group/$($script.Name)")
        }
    }
}

$assetsRoot = Join-Path $repoRoot 'assets'
if (Test-Path -LiteralPath $assetsRoot) {
    foreach ($asset in Get-ChildItem -LiteralPath $assetsRoot -Recurse -File) {
        if ($asset.Name -eq 'AGENTS.md') { continue }
        $relative = [IO.Path]::GetRelativePath($assetsRoot, $asset.FullName).Replace('\', '/')
        if ($relative -notmatch '^(?:[a-z0-9]+(?:_[a-z0-9]+)*/)*[a-z0-9]+(?:_[a-z0-9]+)*\.[a-z0-9]+$') {
            $errors.Add("素材路径不符合小写下划线命名：assets/$relative")
        }
    }
}

if ($errors.Count -gt 0) {
    foreach ($message in $errors) { Write-Host "错误：$message" }
    throw "静态检查失败，共 $($errors.Count) 项"
}
Write-Host "静态检查通过：$($documents.Count) 份文档、规则链接与脚本路径"
