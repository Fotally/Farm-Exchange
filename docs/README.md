# 文档导航

本库正文使用中文，路径使用小写英文和连字符。仓库首页介绍运行入口；本页负责定位文档。文件命名与内容归属由 docs/AGENTS.md 约束。

## 游戏规则

- [作物与选种](gameplay/production/crop-growth.md)：六种作物、成熟时间和切换规则。
- [加工](gameplay/production/processing.md)：对应场地、加工时间与投入损失。
- [出售与价格](gameplay/trading/sales.md)：当日定价、库存与结算。
- [开局与购地](gameplay/land/opening-and-unlock.md)：预置地块、建造与扩张。
- [地图与操作](gameplay/world/map-and-camera.md)：坐标、选择、镜头与画面含义。

## 代码架构

- [FarmGame 接口](architecture/game-state/farm-game/interface-farm-game.md)及[推进顺序](architecture/game-state/farm-game/implementation-tick-order.md)。
- [市场价格接口](architecture/market/market-price-curve/interface-market-price-curve.md)及[曲线实现](architecture/market/market-price-curve/implementation-bounded-curve.md)。
- [WorldMap 接口](architecture/world/world-map/interface-world-map.md)及[分块缓存实现](architecture/world/world-map/implementation-chunk-cache.md)。
- [镜头输入接口](architecture/world/camera-controller/interface-camera-controller.md)。

列出全部接口文档：`rg --files docs/architecture -g 'interface-*.md'`。新增模块时按模块路径添加具名接口文档；实现细节另写 `implementation-` 前缀文件。

## 项目协作与依据

- [系统规划](project/roadmap.md)、[构建与验收](project/build-and-validation.md)、[GitHub 协作](project/contribution-workflow.md)。
- [macOS 构建与验收](project/macos-build.md)：Universal 2 导出、CI 和应用包启动。
- [市场曲线调研](research/market-price-curve.md)、[测试分类调研](research/test-taxonomy.md)。
- [EditorConfig 检查](static-checks/editorconfig.md)、[CI 静态检查](static-checks/ci.md)。
- [规则加载与链接对应表](../.codex/rule-loading.md)。
