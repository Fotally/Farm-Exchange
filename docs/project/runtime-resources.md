# 运行资源与实际 PCK 验收

关联 [#190](https://github.com/Fotally/Farm-Exchange/issues/190)。资源像素、来源授权及历史文件保持；分发只包含运行需要的资源。来源和目录约定分别见[素材来源](asset-sources.md)、[素材组织](asset-organization.md)。

## 资源归属与导出选择

`export_presets.cfg` 集中维护四个预设的导出选择。Windows/macOS 正式预设继续使用 `all_resources`，保留 C# 代码动态拼接的资源路径；不改成只跟随场景静态依赖。

| 分类 | 仓库职责 | 正式与开发包行为 |
| --- | --- | --- |
| `assets/gameplay/` 当前图像、20 名 NPC 的五动作和 SpriteFrames、`assets/ui/` | 经营画面、独立角色预览及 UI 的实际运行资源 | 两类包保留，含七作物三档四向、建筑/土壤四向、道路、水域及局部动效 |
| 天气/地形/岸线/道路四份 JSON | 动态读取的运行配置 | 四预设的 `include_filter` 明确保留 |
| `docs/` | 中文说明、截图、原型与历史研究附件 | 四预设统一排除 `docs/*`，同时排除 Godot 随附件产生的导入记录及纹理 |
| `assets/gameplay/npc/npc_animation_*.png` | 已停用的 20 张历史 NPC 图集 | 四预设明确排除，历史文件与来源说明继续保留在仓库 |
| `assets/development/`、开发脚本与场景 | DEBUG 开发窗口及参数化流程 | 正式包排除；Dev 预设保留开发入口及已有三份内置 JSON |
| 原始 ZIP、`assets/source/`、旧 `assets/npc/` 示例 | 归档或参考 | 四预设继续排除 |
| `logs/schema-v1.md` | Windows 运行日志的独立阅读说明 | 分发准备从同提交 `docs/project/runtime-log-schema-v1.md` 复制，不依赖 PCK 包含 `docs/` |

开发包需要当前五动作 NPC 和参数化测试配置；当前没有读取文档截图或旧图集的开发预览入口，因此开发包也不携带它们。以后新增实际预览消费方时，应按明确需求调整预设、检查和本页，不建立旧新路径兜底加载。

## 实际资源包检查接口

`tools/Test-RuntimePack.ps1 -PackPath <PCK 或分发 ZIP> [-ReportPath <JSON>]` 是 Windows 分发、macOS CI 和发布共用的完整资源检查入口。它读取固定引擎实际生成的未加密、非稀疏 PCK v4 目录；不以预设文本代替导出结果。输入 ZIP 时直接读取唯一的 Windows 根目录 PCK 或 macOS 应用 `Contents/Resources/*.pck`，不改写 ZIP。

检查同时包含两面：

- 禁止任何 `docs/` 条目、旧 `npc_animation_*` 条目与其导入纹理；根据同提交文档图片 `res://` 路径的 MD5 识别 `.godot/imported/` 中的文档纹理，即使其 `.import` 条目缺失也拒绝。
- 根据同提交 `assets/gameplay/` 和 `assets/ui/` 的有效 PNG/SVG/SpriteFrames 检查资源完整性，只有已声明停用的 NPC 图集除外；另要求主场景、角色场景、两份世界 shader、四份运行 JSON 和 `project.binary`。对 Godot 导出后的 `.import` 或 `.remap`，读取包内记录并确认每个实际导入目标存在且非空，避免只保留源路径占位记录。

因此新增当前资源会自动进入同提交必需集合；目录中的历史资源必须有明确归属，不能随意混在当前运行目录。检查器只持有资源选择、PCK 格式及完整性规则，调用者不复制这些规则。它不解码纹理或证明场景正常启动；正式引擎导出及真实启动仍分别验收。

Windows 的 `Invoke-WindowsDistribution.ps1` 在启动前、启动后和最终临时 ZIP 上检查实际 PCK。失败停止分发且不生成最终 ZIP，已有日志保全及独立 schema 规则继续生效。macOS CI 在导出 ZIP 后检查，发布复用同提交工具再次检查下载的原 ZIP；两处都保存独立资源清单，发布仍复制原 ZIP 字节。

## 夹具与证据

```powershell
./tools/Test-RuntimePackChecks.ps1
./tools/Test-ValidationArtifacts.ps1
./tools/Test-RuntimePack.ps1 -PackPath build/windows/FarmExchange.pck -ReportPath build/validation/windows/runtime-pack.json
./tools/Test-RuntimePack.ps1 -PackPath build/macos/FarmExchange.zip -ReportPath build/validation/macos/runtime-pack.json
```

第一项生成真实二进制 PCK v4 正反夹具，覆盖文档导入记录、仅文档纹理、旧图集记录、仅旧图集纹理、缺运行 JSON、缺 q3 作物、缺当前 NPC 动作、导入目标缺失；同时核验 ZIP 内的合格/不合格 PCK 及 ZIP 原字节保持。第二项由测试 CI 调用，进一步验证三种不合格 PCK 均使真实 Windows 分发命令拒绝生成最终 ZIP。工具夹具内容仅用于机械契约，不能冒充可运行游戏。

报告包含每条资源路径、偏移、有效载荷字节及 MD5，以及整个 PCK 的 SHA-256、条目数和文件字节。`ContainerBytes` 对 PCK 输入表示 PCK 文件大小，对 ZIP 输入表示完整 ZIP 大小；`PckBytes` 始终是内部 PCK 大小，两者不可混写为程序集大小。程序集和完整分发目录/ZIP 的实测大小另行记录。

修复前的包可加 `-InventoryOnly` 保存清单与禁止资源数量/有效载荷字节；报告明确为 `Validation=NotRun`，该选项仅用于前后对照，正式 CI 和分发入口均不使用。正常检查失败也会先保存目录与失败原因，再抛出错误。前后清单、导出输出和启动日志留在 `build/validation/`，不把每次重复证据加入文档库，不据包体减少声称 FPS 提升。

本地 Windows 可以核验两平台预设导出的实际 PCK，但 macOS 应用导出、双架构和实际启动仍以 macOS runner 的结果为准；不能用一个预设的成功替代另一平台验收。
