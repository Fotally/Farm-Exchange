# CultivationWindow 接口

对应 `scripts/ui/CultivationWindow.cs`，继承[可拖动窗口](../draggable-window/interface-draggable-window.md)，从开局底部“年度耕作表”与农田详情入口打开。

- `Refresh(FarmGame)` 读取共享表、农田引用与当前日期；不提交命令、不推进经营，不覆盖名称、模式、作物条草稿、输入焦点或农田勾选。
- `SaveRequested(int?, CultivationPlanRequest)` 请求创建或编辑共享表；空 ID 创建，已有 ID 更新原表。每条稳定 ID 在移动、跨季折行与保存时保持。
- `ApplyRequested(int, IReadOnlyList<Vector2I>)` 报告已保存表与勾选农田锚点；表尚未保存或未勾选农田时在窗口内说明原因。
- `ShowCommandResult(CultivationCommandResult)` 与 `ShowApplyResult(string?)` 展示经营入口的真实结果。保存成功关联返回的表 ID，草稿留在原控件中。

窗口只持有编辑草稿与选择状态。新增及移动时，由窗口构造完整候选请求，在时间图 `CanDrop` 阶段调用 `FarmGame.CheckCultivationPlan`；移动替换原 ID 对应的一条，不同时保留旧位置。落位回调再次验证同一候选，成功后才一次更新草稿，保存时继续重验。拒绝保持原条、草稿与正式表，显示经营入口的具体原因；UI 不重新计算季节、重叠或间隔约束。适季开始但跨禁生边界的条仍允许落位及保存，红色圆角轮廓标识风险；禁生季起点拒绝，不自动移动或换作物。表级模式选项作用于整张表。列表展示真实引用田数量；保存按钮旁提示影响几块引用田，农田勾选行展示当前引用名称。

执行日期与实际进度分开：时间图显示计划日期及今日标记，农田详情显示实际生长剩余游戏天数（向上取一位小数）、待水时完成日期未确定、预备作物与日期或休耕。具体折行与动效见[年度时间图实现](implementation-annual-timeline.md)，玩家操作见[耕作表操作](../../../gameplay/production/seasonal-cultivation-ui.md)。

## 验收入口

`tests/e2e/test_cultivation_window.tscn` 使用真实 `Main` 与经营入口验证拖入、移动、移除、冲突拒绝、风险保存、表级模式、批量引用、手动接管和刷新草稿；两种手动窗口打开后从详情移除本田，验证窗口随生命周期关闭。布局检查验证 1280×720 可用区域、固定按钮与输入焦点；暂停时保存表改变当期空田作物，验证可见地图块同步，有渲染时还比较工作中心实际像素。

第一轮原生拖动到冬春落点、释放之前检查预览中心与实际鼠标位置误差不超过1.5逻辑像素、仍在拖动且经营日期未推进；有渲染时等该帧实际绘制完成，保存整根直条预览 `build/issue86-ui-validation/cultivation-drag-preview.png`。落位分段图保存在同目录 `cultivation-window.png`。真实原生拖放断言非法悬停不会触发落位事件，保存后原条保持；另检查自身替换、冬春重叠与一天间隔合法对照。

有窗口时，真实鼠标悬停短萝卜和窄冬春片段，按 Godot 标准提示延迟等待提示出现，验证整轮日期、完整周期及窗口内可读范围，保存 `short-radish-tooltip.png` 和 `winter-spring-tooltip.png`；移开后检查提示消失。能容纳完整信息的条直接条内显示，无需悬浮。headless 保留拖动及日期断言，不等待渲染、不保存截图，也不伪造原生提示已出现。

有窗口的原生鼠标事件同时定位系统鼠标，确保拖动预览与释放使用同一落点；每轮拖动结束或异常退出时恢复原屏幕鼠标位置。坐标换算与 headless 差异见上述年度时间图实现。
