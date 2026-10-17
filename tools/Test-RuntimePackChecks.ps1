param([string]$EvidenceDirectory = 'build/runtime-pack-checks')

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runRoot = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory, $repoRoot)) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
. (Join-Path $PSScriptRoot 'runtime-pack-fixture/New-RuntimePack.ps1')
$checker = Join-Path $PSScriptRoot 'Test-RuntimePack.ps1'
$doc = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs') -File -Recurse -Filter '*.png' | Select-Object -First 1
$docResource = 'res://' + [IO.Path]::GetRelativePath($repoRoot, $doc.FullName).Replace('\', '/')
$docHash = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes($docResource))).ToLowerInvariant()
foreach ($case in @(
    @{ Name = 'clean'; Error = ''; Remove = @(); Add = @{} },
    @{ Name = 'document'; Error = '混入文档附件'; Remove = @(); Add = @{ 'docs/screenshots/unexpected.png.import' = 'unexpected' } },
    @{ Name = 'document-texture-only'; Error = '混入文档附件'; Remove = @(); Add = @{ ".godot/imported/$($doc.Name)-$docHash.ctex" = 'unexpected' } },
    @{ Name = 'retired-npc'; Error = '退役 NPC'; Remove = @(); Add = @{ 'assets/gameplay/npc/npc_animation_001.png.import' = 'unexpected' } },
    @{ Name = 'retired-texture-only'; Error = '退役 NPC'; Remove = @(); Add = @{ '.godot/imported/npc_animation_001.png-00000000000000000000000000000000.ctex' = 'unexpected' } },
    @{ Name = 'missing-json'; Error = '必需运行资源'; Remove = @('assets/gameplay/weather/weather_rules.json'); Add = @{} },
    @{ Name = 'missing-q3'; Error = '必需运行资源'; Remove = @('assets/gameplay/crops/wheat_growing_01_q3.png.import'); Add = @{} },
    @{ Name = 'missing-npc'; Error = '必需运行资源'; Remove = @('assets/gameplay/npc/npc001/npc001_idle.png.import'); Add = @{} },
    @{ Name = 'missing-import-target'; Error = '导入目标'; Remove = @(); Add = @{ 'assets/ui/icons/leaf.svg.import' = "[remap]`npath=`"res://.godot/imported/missing.ctex`"`n" } }
)) {
    $pack = Join-Path $runRoot "$($case.Name).pck"
    New-RuntimePackFixture -Path $pack -RepoRoot $repoRoot -Remove $case.Remove -Add $case.Add
    $caught = $null
    try { & $checker -PackPath $pack -ReportPath (Join-Path $runRoot "$($case.Name).json") }
    catch { $caught = $_.Exception.Message }
    if ($case.Error) {
        if (-not $caught -or -not $caught.Contains($case.Error)) { throw "$($case.Name) 应拒绝 [$($case.Error)]，实际：$caught" }
    } elseif ($caught) { throw $caught }
    Write-Host "通过 PCK 夹具：$($case.Name)"
}

# 发布从 ZIP 内直接读取实际 PCK，同样拒绝不合格内容，不重新压缩 macOS 包。
foreach ($name in @('clean', 'document', 'missing-json')) {
    $zipPath = Join-Path $runRoot "$name.zip"
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $runRoot "$name.pck"), 'FarmExchange.app/Contents/Resources/FarmExchange.pck') }
    finally { $zip.Dispose() }
    $before = (Get-FileHash -LiteralPath $zipPath).Hash
    $caught = $null
    try { & $checker -PackPath $zipPath -ReportPath (Join-Path $runRoot "$name-zip.json") }
    catch { $caught = $_.Exception.Message }
    if (($name -eq 'clean' -and $caught) -or ($name -ne 'clean' -and -not $caught)) { throw "ZIP 内 PCK 检查结果错误：$name -> $caught" }
    if ($before -cne (Get-FileHash -LiteralPath $zipPath).Hash) { throw 'PCK 检查修改了 macOS ZIP 字节' }
}
Write-Host "运行资源 PCK 契约通过；证据：$runRoot"
