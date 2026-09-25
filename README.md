# Farm Exchange

Farm Exchange 是使用 Godot 4.7.2 Mono 与 C# 制作的等距视角放置挂机游戏。玩家在 128×128 的地图上建农田，工人自动照料作物；原料交给对应场地加工，加工品出售后可建造更多建筑。当前有六种作物及对应加工场地。存档与离线收益尚待确认。

## 运行项目

使用 Godot 4.7.2 Mono 打开根目录的 project.godot，运行主场景 scenes/main.tscn。本地指定引擎位于 E:\Godot\Godot_v4.7.2-stable_mono_win64。C# 项目目标框架为 .NET 8；Windows 构建及导出模板配置见[构建与验收](docs/project/build-and-validation.md)。

## 开发与文档

在仓库根目录运行完整 headless 场景测试与覆盖率检查：

```powershell
./tools/Run-Tests.ps1 -GodotConsole 'E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
```

玩法规则、模块接口、调研与静态检查见[文档导航](docs/README.md)。已确认范围与待确认事项见[系统规划](docs/project/roadmap.md)。

## 参与修改

先关联 GitHub issue，在 dev 分支完成代码、中文文档和验收；验证通过后提交 dev → main Pull Request，等待人工合并。详细步骤见[GitHub 协作流程](docs/project/contribution-workflow.md)。PR 描述按仓库模板填写；Agent 使用仓库内的 farm-exchange-submit-pr skill 执行提交流程。

## 许可证与素材

项目尚未确定开源许可证。后续引入第三方素材时，按[素材命名与来源规范](.claude/rules/assets.md)记录授权和适用文件。
