---
paths:
  - "tests/**"
---

# 测试命名与范围

- C# 测试脚本采用 Test加对象名 的 PascalCase，例如 TestFarmGame.cs；对应场景使用小写 snake_case，例如 test_farm_game.tscn。
- tests/unit 验证单个模块的公开行为，tests/integration 验证模块连接，tests/e2e 验证主场景经营流程，tests/performance 验证负载或图形帧率。测试类型和触发条件见 docs/research/test-taxonomy.md。
- 通过公开接口验证行为；不要依赖模块的私有实现顺序。代码行为变化时更新对应的必要测试，并遵守现有覆盖率与性能验收要求。
