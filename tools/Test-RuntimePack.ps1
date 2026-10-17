param(
    [Parameter(Mandatory = $true)]
    [string]$PackPath,
    [string]$ReportPath,
    [switch]$InventoryOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pack = (Resolve-Path -LiteralPath $PackPath).Path
$archive = $null
$stream = $null
$reader = $null
try {
    # 分发 ZIP 保持原字节，只把唯一资源包读入可定位的流。
    if ([IO.Path]::GetExtension($pack) -ieq '.zip') {
        $archive = [IO.Compression.ZipFile]::OpenRead($pack)
        $packs = @($archive.Entries | Where-Object { $_.FullName -cmatch '^(FarmExchange\.pck|[^/]+\.app/Contents/Resources/[^/]+\.pck)$' })
        if ($packs.Count -ne 1) { throw "分发 ZIP 必须包含唯一资源 PCK：$pack" }
        $stream = [IO.MemoryStream]::new()
        $inputStream = $packs[0].Open()
        try { $inputStream.CopyTo($stream) } finally { $inputStream.Dispose() }
        $stream.Position = 0
    } else {
        $stream = [IO.File]::OpenRead($pack)
    }
    $reader = [IO.BinaryReader]::new($stream, [Text.Encoding]::UTF8, $true)
    # 固定引擎的独立、未加密 PCK v4；格式变更须显式更新检查器。
    # https://github.com/godotengine/godot/blob/master/core/io/file_access_pack.cpp
    if ($reader.ReadUInt32() -ne 0x43504447 -or $reader.ReadUInt32() -ne 4) { throw "资源包不是 Godot PCK v4：$pack" }
    $engine = '{0}.{1}.{2}' -f $reader.ReadUInt32(), $reader.ReadUInt32(), $reader.ReadUInt32()
    $flags = $reader.ReadUInt32()
    if (($flags -band 1) -ne 0 -or ($flags -band 4) -ne 0) { throw '资源包不能使用加密目录或稀疏布局' }
    $fileBase = $reader.ReadUInt64()
    $directoryOffset = $reader.ReadUInt64()
    if ($directoryOffset -gt $stream.Length - 4) { throw 'PCK 目录超出文件范围' }
    $stream.Position = $directoryOffset
    $count = $reader.ReadUInt32()
    $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    for ($index = 0; $index -lt $count; $index++) {
        $pathLength = $reader.ReadUInt32()
        if ($pathLength -gt $stream.Length - $stream.Position) { throw 'PCK 目录路径超出文件范围' }
        $name = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($pathLength)).TrimEnd([char]0) -creplace '^res://', ''
        $offset = $reader.ReadUInt64() + $fileBase
        $size = $reader.ReadUInt64()
        $md5 = [Convert]::ToHexString($reader.ReadBytes(16)).ToLowerInvariant()
        $entryFlags = $reader.ReadUInt32()
        if ($entryFlags -ne 0 -or $offset -gt $stream.Length -or $size -gt $stream.Length - $offset) {
            throw "PCK 条目无效或使用不支持的标志：$name"
        }
        if (-not $entries.TryAdd($name, [pscustomobject]@{ Path = $name; Offset = $offset; Bytes = $size; Md5 = $md5 })) {
            throw "PCK 目录重复路径：$name"
        }
    }

    $forbiddenHashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs') -File -Recurse | Where-Object { $_.Extension -in @('.png', '.svg', '.jpg', '.jpeg', '.webp') }) {
        $source = 'res://' + [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
        [void]$forbiddenHashes.Add([Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes($source))).ToLowerInvariant())
    }
    $forbidden = @($entries.Values | Where-Object {
        $_.Path -cmatch '^docs/|(^|/)npc_animation_[^/]*' -or
        ($_.Path -cmatch '^\.godot/imported/.*-([a-f0-9]{32})\.' -and $forbiddenHashes.Contains($Matches[1]))
    } | Sort-Object Path)

    # 当前 gameplay 与 UI 目录里的图像/SpriteFrames 都是运行资源，只有已退役 NPC 图集除外。
    # 从同提交源文件建立要求，避免 all_resources 改成静态依赖时漏掉动态拼接的路径。
    $required = [Collections.Generic.List[string]]::new()
    foreach ($directory in @('assets/gameplay', 'assets/ui')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot $directory) -File -Recurse | Where-Object {
            $_.Extension -in @('.png', '.svg', '.tres') -and $_.Name -cnotmatch '^npc_animation_'
        }) { $required.Add([IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')) }
    }
    foreach ($path in @(
        'project.binary', 'scenes/main.tscn', 'scenes/npc_character.tscn',
        'scripts/world/rooted_wind.gdshader', 'scripts/world/water_ripples.gdshader',
        'assets/gameplay/weather/weather_rules.json', 'assets/gameplay/terrain/terrain_rules.json',
        'assets/gameplay/terrain/shoreline_templates.json', 'assets/gameplay/terrain/roads/atlas_regions.json'
    )) { $required.Add($path) }

    function Assert-PackedResource {
        param([string]$Path)
        if ($entries.ContainsKey($Path) -and $entries[$Path].Bytes -gt 0) { return }
        $metadataPath = if ($Path -cmatch '\.(png|svg)$') { "$Path.import" } else { "$Path.remap" }
        if (-not $entries.ContainsKey($metadataPath) -or $entries[$metadataPath].Bytes -eq 0) { throw "资源包缺少必需运行资源：$Path" }
        $metadata = $entries[$metadataPath]
        $stream.Position = $metadata.Offset
        $content = [Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$metadata.Bytes))
        $targets = [regex]::Matches($content, '(?m)^path(?:\.[a-zA-Z0-9_]+)?="res://([^"]+)"')
        if ($targets.Count -eq 0) { throw "运行资源没有有效导入目标：$metadataPath" }
        foreach ($target in $targets) {
            $targetPath = $target.Groups[1].Value
            if (-not $entries.ContainsKey($targetPath) -or $entries[$targetPath].Bytes -eq 0) {
                throw "资源包缺少必需运行资源的导入目标：$Path -> $targetPath"
            }
        }
    }

    $validationFailure = $null
    if (-not $InventoryOnly) {
        try {
            if ($forbidden.Count -gt 0) { throw "资源包混入文档附件或退役 NPC 图集：$($forbidden[0].Path)" }
            foreach ($path in $required) { Assert-PackedResource $path }
        } catch { $validationFailure = $_.Exception.Message }
    }
    $stream.Position = 0
    $result = [ordered]@{
        Container = $pack
        ContainerBytes = (Get-Item -LiteralPath $pack).Length
        PckBytes = $stream.Length
        PckSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
        Engine = $engine
        Validation = if ($InventoryOnly) { 'NotRun' } elseif ($validationFailure) { 'Failed' } else { 'Passed' }
        Failure = $validationFailure
        EntryCount = $entries.Count
        RequiredResourceCount = $required.Count
        ForbiddenEntryCount = $forbidden.Count
        ForbiddenPayloadBytes = [long]($forbidden | Measure-Object Bytes -Sum).Sum
        Entries = @($entries.Values | Sort-Object Path)
    }
    if ($ReportPath) {
        $report = [IO.Path]::GetFullPath($ReportPath, (Get-Location).Path)
        New-Item -ItemType Directory -Path (Split-Path -Parent $report) -Force | Out-Null
        $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding utf8
    }
    if ($validationFailure) { throw $validationFailure }
    Write-Host "PCK $($result.Validation)：$pack；$($result.EntryCount) 条，$($result.PckBytes) 字节，禁止资源 $($result.ForbiddenEntryCount) 条"
} finally {
    if ($reader) { $reader.Dispose() }
    if ($stream) { $stream.Dispose() }
    if ($archive) { $archive.Dispose() }
}
