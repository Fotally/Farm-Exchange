$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureRoot = Join-Path $repoRoot ("build/rule-references-" + [guid]::NewGuid().ToString('N'))
$checker = Join-Path $PSScriptRoot 'Test-RuleReferences.ps1'

function Assert-ReferenceFailure([string[]]$Expected) {
    $message = ''
    try { & $checker -RepositoryRoot $fixtureRoot }
    catch { $message = $_.Exception.Message }
    if ($message -eq '') { throw '负面夹具未被拒绝' }
    foreach ($part in $Expected) {
        if (-not $message.Contains($part)) { throw "诊断缺少 ${part}：$message" }
    }
}

try {
    foreach ($directory in @('.codex', '.claude/rules', 'scripts', 'docs/static-checks')) {
        New-Item -ItemType Directory -Path (Join-Path $fixtureRoot $directory) -Force | Out-Null
    }
    $source = Join-Path $fixtureRoot '.claude/rules/scripts.md'
    $entry = Join-Path $fixtureRoot 'scripts/AGENTS.md'
    $mappingPath = Join-Path $fixtureRoot '.codex/rule-links.json'
    '[{"source":".claude/rules/scripts.md","links":["scripts/AGENTS.md"]}]' | Set-Content -LiteralPath $mappingPath
    '# 注释示例' | Set-Content -LiteralPath (Join-Path $fixtureRoot 'docs/static-checks/interface-comments.md')
    '[注释](../../docs/static-checks/interface-comments.md)' | Set-Content -LiteralPath $source

    # Windows 复用同卷真实符号链接节点，不要求为负面夹具提升创建符号链接的权限。
    if ($IsWindows) {
        New-Item -ItemType HardLink -Path $entry -Value (Join-Path $repoRoot 'scripts/AGENTS.md') | Out-Null
    }
    else {
        New-Item -ItemType SymbolicLink -Path $entry -Target '../.claude/rules/scripts.md' | Out-Null
    }
    $link = Get-Item -LiteralPath $entry -Force
    if ($link.LinkType -ne 'SymbolicLink' -or
        [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $entry) $link.Target)) -ne $source) {
        throw '夹具必须使用目标正确的真实符号链接'
    }
    $wrongTarget = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $entry) '../../docs/static-checks/interface-comments.md'))
    Assert-ReferenceFailure @('scripts/AGENTS.md', '../../docs/static-checks/interface-comments.md', $wrongTarget)

    '仓库根路径：`docs/static-checks/interface-comments.md`' | Set-Content -LiteralPath $source
    & $checker -RepositoryRoot $fixtureRoot
    '仓库根路径：`docs/static-checks/missing.md`' | Set-Content -LiteralPath $source
    Assert-ReferenceFailure @('.claude/rules/scripts.md', 'scripts/AGENTS.md', 'docs/static-checks/missing.md', (Join-Path $fixtureRoot 'docs/static-checks/missing.md'))

    # 普通相对链接仍按各自入口解析；外网和纯锚点不在本检查范围。
    '[根目录](../) [外网](https://example.invalid/) [锚点](#example)' | Set-Content -LiteralPath $source
    & $checker -RepositoryRoot $fixtureRoot
    '[{"source":".claude/rules/scripts.md","links":["scripts/missing.md"]}]' | Set-Content -LiteralPath $mappingPath
    Assert-ReferenceFailure @('scripts/missing.md', (Join-Path $fixtureRoot 'scripts/missing.md'))
    Write-Host '规则引用夹具通过：正确符号链接下的入口失效、根路径、普通相对路径及缺失入口'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $allowedParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'build')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($allowedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理非夹具目录：$resolvedFixture"
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
