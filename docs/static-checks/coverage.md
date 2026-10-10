# 自动行覆盖率门禁

[统一测试入口](../../tools/Run-Tests.ps1)在收集 Cobertura 后调用 [Test-Coverage.ps1](../../tools/Test-Coverage.ps1)，本地与现有 Windows CI 使用同一判定。总体和每个 `scripts/` 一级目录模块均必须达到 80%，恰好 80% 通过；判定采用整数行数，不使用四舍五入后的显示百分比。

模块列表来自仓库实际 `scripts/` 一级目录，不维护硬编码名单。读取报告 class 下的文件行记录，以规范化文件路径和行号去重，同一行任一类型命中即计覆盖。绝对文件路径直接定位；相对路径按 Cobertura `sources/source` 解析，没有 source 时相对仓库根目录；兼容正反斜线。同一文件的 method 重复记录不另外累加。仅统计当前仓库 `scripts/` 内真实文件，总体由各模块的去重计数汇总，不采信根节点 `line-rate`，也不平均文件或类型百分比。

每次输出模块及总体的“已覆盖行 / 有效行”和百分比，并返回包含 `Module`、`Covered`、`Valid`、`Percent` 的结果对象。报告不存在、XML 无效、没有有效业务行、某一级模块没有行记录、行号或命中数无效、总体或模块低于 80% 都会失败。失败提示报告路径和具体模块或记录原因，空模块不会被默认为达标。报告必须与所选仓库对应，不从其他检出目录的路径推断本地源码。

独立核对已有报告：

```powershell
pwsh -NoProfile -File tools/Test-Coverage.ps1 -ReportPath coverage/coverage.cobertura.xml
```

可用 `-RepositoryRoot` 明确指定待核对的仓库根目录，默认使用工具所在仓库。该命令不编译、不运行 Godot，也不改报告或源码。

[Test-CoverageGate.ps1](../../tools/Test-CoverageGate.ps1)在临时目录生成独立 XML 和源码路径夹具，经相同工具入口核验：总体 90% / 模块 0%、恰好 80%、低于 80%、重复类型及路径的行合并、source 相对路径、缺失报告、空业务数据、缺失模块和动态新增模块。统一测试入口先运行这些夹具，夹具失败会中断本地与 CI 验收。也可单独执行：

```powershell
pwsh -NoProfile -File tools/Test-CoverageGate.ps1
```

本改动不提高阈值、不新增业务源码排除项、不新增分支覆盖率门槛；正式业务测试和历史记录见[测试与验收](../project/testing.md)。
