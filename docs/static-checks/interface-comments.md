# 接口 XML 注释格式

接口注释用换行区分业务动作、参数与返回结果。`/**` 和 `*/` 分别独占一行；`summary` 独占一行，每个 `param` 独占一行，`returns` 独占一行。已有 `remarks` 等元素也分别独占一行。

例如 `FarmGame.CheckCultivationPlan`：

```csharp
/**
 * <summary>检查完整草稿的年度排程冲突与禁生季风险。</summary>
 * <param name="request">名称、表级模式和年度作物条。</param>
 * <returns>正常拒绝或风险条编号。</returns>
 */
```

多个参数按函数参数顺序逐行描述。每个元素的起始标签、说明与结束标签在同一行，块内部沿用 ` * ` 前缀。格式修正保留原注释文字、元素顺序与接口约定；返回值描述仍区分成功和正常拒绝。

这项排版约定由人工审查核对；`dotnet format` 检查范围见 [EditorConfig](editorconfig.md)。代码公开约定与业务过程仍在对应的 `docs/architecture/` 接口及实现文档中维护。
