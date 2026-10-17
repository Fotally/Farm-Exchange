# 只供工具契约测试使用；内容是可解析的 PCK v4，不是可启动的 Godot 游戏。
function New-RuntimePackFixture {
    param([string]$Path, [string]$RepoRoot, [string[]]$Remove = @(), [hashtable]$Add = @{})
    $files = [Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
    foreach ($directory in @('assets/gameplay', 'assets/ui')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $RepoRoot $directory) -File -Recurse | Where-Object {
            $_.Extension -in @('.png', '.svg', '.tres') -and $_.Name -cnotmatch '^npc_animation_'
        }) {
            $name = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
            if ($file.Extension -in @('.png', '.svg')) {
                $hash = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes("res://$name"))).ToLowerInvariant()
                $target = ".godot/imported/$($file.Name)-$hash.ctex"
                $files["$name.import"] = [Text.Encoding]::UTF8.GetBytes("[remap]`npath=`"res://$target`"`n")
                $files[$target] = [byte[]]@(1)
            } else { $files[$name] = [byte[]]@(1) }
        }
    }
    foreach ($name in @(
        'project.binary', 'scenes/main.tscn', 'scenes/npc_character.tscn',
        'scripts/world/rooted_wind.gdshader', 'scripts/world/water_ripples.gdshader',
        'assets/gameplay/weather/weather_rules.json', 'assets/gameplay/terrain/terrain_rules.json',
        'assets/gameplay/terrain/shoreline_templates.json', 'assets/gameplay/terrain/roads/atlas_regions.json'
    )) { $files[$name] = [byte[]]@(1) }
    foreach ($name in $Remove) { [void]$files.Remove($name) }
    foreach ($name in $Add.Keys) { $files[$name] = [Text.Encoding]::UTF8.GetBytes($Add[$name]) }

    $stream = [IO.File]::Create($Path)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint32]0x43504447)
        foreach ($value in @(4, 4, 7, 2, 2)) { $writer.Write([uint32]$value) }
        $writer.Write([uint64]0)
        $writer.Write([uint64]0) # 目录位置稍后回填。
        $offsets = @{}
        foreach ($name in $files.Keys) {
            $offsets[$name] = $stream.Position
            $writer.Write($files[$name])
        }
        $directoryOffset = $stream.Position
        $writer.Write([uint32]$files.Count)
        foreach ($name in $files.Keys) {
            $encoded = [Text.Encoding]::UTF8.GetBytes("res://$name")
            $writer.Write([uint32]$encoded.Length)
            $writer.Write($encoded)
            $writer.Write([uint64]$offsets[$name])
            $writer.Write([uint64]$files[$name].Length)
            $writer.Write([Security.Cryptography.MD5]::HashData($files[$name]))
            $writer.Write([uint32]0)
        }
        $stream.Position = 32
        $writer.Write([uint64]$directoryOffset)
    } finally { $writer.Dispose(); $stream.Dispose() }
}
