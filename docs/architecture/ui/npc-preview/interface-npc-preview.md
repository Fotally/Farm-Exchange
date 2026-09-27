# NpcPreview 对外接口

对应类型：`FarmExchange.UI.NpcPreview`，代码位于 `scripts/ui/NpcPreview.cs`；独立场景为 `scenes/npc_preview.tscn`。在仓库根目录用指定 Godot 控制台程序运行：

```powershell
E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --path . scenes/npc_preview.tscn
```

| 输入或输出 | 行为 |
| --- | --- |
| WASD、方向键 | 将输入方向交给 `NpcCharacter.SetMoveDirection`，移动与动画一致。 |
| Q、E | 按来源序号循环选择上一位或下一位角色；20 张图集共用同一套动画。 |
| 跟随镜头 | 以角色位置为目标，沿用示例的平滑跟随和矩形边界；预览倍率为 2。 |
| 页面标签 | 显示当前序号、总数和图集文件名。 |

预览场景复现下载包的操作方式，使用本工程的 C# 与 Godot 场景。它独立于 `scenes/main.tscn`；主地图的镜头快捷键和经营循环不因预览发生变化。图片出处见[素材来源](../../../project/asset-sources.md)。
