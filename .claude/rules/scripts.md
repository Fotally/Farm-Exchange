---
paths:
  - "scripts/**/*.cs"
---

# C# 游戏代码规则

- 脚本目录与命名空间对应：scripts/gameplay → FarmExchange.Gameplay，scripts/market → FarmExchange.Market，scripts/world → FarmExchange.World，scripts/ui → FarmExchange.UI。C# 文件名与主要类型名一致并使用 PascalCase。
- 类型、公开成员、方法、常量使用 PascalCase；参数与局部变量使用 camelCase；私有实例字段使用 _camelCase。金额使用 Cents、tick 数使用 Ticks、格坐标使用 Cell 等明确单位或坐标域。
- 跨模块只依赖对方公开接口，内部状态和绘制缓存留在拥有它的模块。模块接口是实际公开约定，不要求一一对应 C# interface 类型。
- 只有出现真实的可替换实现时才声明 C# interface 类型；一旦声明，跨模块调用方按该类型依赖它，场景组装处接入具体实现。
- 修改模块公开约定时更新对应的 docs/architecture/ 接口文档；修改玩家规则时更新 docs/gameplay/。不为尚未出现的情况增加接缝或兜底。
