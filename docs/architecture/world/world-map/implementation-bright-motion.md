# 清亮 v2 局部动效实现与验收约定

履行 [WorldMap Interface](interface-world-map.md)，关联 [#108](https://github.com/Fotally/Farm-Exchange/issues/108)。**状态：2026-10-07 已完成WorldMap整合、独立审查及实际像素/性能/引擎验收。** 结果见[验收记录](../../../project/testing.md)，下述参数对应源码。

## 已确认范围与实际 Seam

本期仅 q0 磨坊、制糖坊、小麦/甘蔗/萝卜三档植株；其他设施沿用清亮 v2 默认静态。用户已确认播种/浇水直接使用人物合成帧，收获/加工只在可见实例显示短促产出 FX；开发高倍率只显示最新有效结果，跳过过期动作，不排队补播。工人采收、搬运、加工和四向动效均不在此范围。

`WorldMap` 以私有 `FacilityVisual` 保存每实例 Soil、Surface 及可见时创建的 `FacilityMotion`，工作中心定位；Soil 固定低层，Surface/Motion 在共同 YSort 层。Motion 仅替换覆盖状态的 Surface，但所有生产设施均可使用短产出。外观 key 不含精确进度，重复配置不重建；离屏不创建 15 株节点与材质。`Main` 只传同局 `SimulationDriver`，不逐栋操作叶片、轮毂或粒子。

`FacilityMotion` 无独立 `_Process`、经营状态或时钟。内部 Interface 为 `ReplacesSurface(plot)` 判断外观替换、`Configure(plot,growthStage)` 配置零起始三档、`Advance(seconds,processing)` 接收已含倍率与暂停的视觉秒和真实加工状态、`ShowResult(result)` 消费 Harvest/Product 并防重复、`ClearResult()` 清除短效果但不回退消费凭据。WorldMap 仅为可见实例调用推进，先验 `IsPresentationResultCurrent` 再启动结果；首次绑定不播放已有结果。已经启动的效果以 `IsPresentationTargetCurrent` 校验原实例，允许跨经营秒自然结束；不拿“最近秒新消费窗口”截断在播片段。保守局部包围范围为 (-128,-208)、大小 256×272。

生产权威继续由 `FarmingSystem` / `ProcessingSystem` 拥有，`FarmGame` 聚合逐实例真实成功结果；动画仅拥有视觉时间和消费记录。与人物作业复用同一经营结果 Seam，不能从“上一帧忙、下一帧空闲”推断完成，因为批次可同一经营秒完成并立即续批。消费记录不属于存档或经营幂等机制。

## 包内资源与两建筑分层

以下路径均相对 `FarmExchange-Bright-Complete-v2/default/motion/`，正式建筑在 `assets/gameplay/buildings/motion/`，植株在 `assets/gameplay/crops/motion/`，映射与许可见各自 `source_notes.md`，程序效果说明在 `assets/gameplay/fx/source_notes.md`。按[素材管理](../../../project/asset-organization.md)分类，不复制独立工程、预览时钟或示例经营状态；清单版本及哈希见[清亮 v2 分析](../../../research/bright-complete-v2.md)。

| 用途 | 实际文件 | 使用约定 |
| --- | --- | --- |
| 磨坊塔身 | `assets/buildings/windmill_body.png` | 256×240，左上偏移 `-(128,176)`；它取代静态整栋图 |
| 磨坊叶片 | `assets/buildings/windmill_sails_256.png` | 16×16 帧排列，每帧 128×128，帧 pivot=(64,64)，中心在建筑画布 `(125,104)` |
| 磨坊轮毂 | `assets/buildings/windmill_hub.png` | 与叶片同中心，固定，不跟帧旋转 |
| 制糖坊主体 | `assets/buildings/sugarworkshop_body.png` | 256×240，pivot=(128,176)；以此为蒸汽基底 |
| 田地 | `assets/ground/field_dry.png`、`field_wet.png` | 224×192，pivot=(112,128)，真实水分选图，全程不形变 |
| 植株风 | `shaders/rooted_wind.gdshader` | 仅给单株采样使用，保留固定根部及透明 padding |
| 收获、产出与蒸汽参考 | `shared_fx/harvest.tscn`、`product_count.tscn`、`steam.tscn` 及 `fx_burst.gd` | 提取必要表现，正式时间和实际数量来自主游戏，不整包运行 |

`docs/building_layers.json` 明确磨坊局部顺序为 body→sails→hub→局部前景 FX；叶片世界偏移为 `(-3,-72)`，中心不可按纹理包围盒猜测。不能在 `default/static/buildings/png/wheat_workshop_q0.png` 上加活动叶片，该整图仍有固定叶片。叶片是预渲染等距平面帧，不用 Sprite 屏幕平面 rotation 旋转。

磨坊实际仅加工时按 0.53 弧度/视觉秒转，缺料/底线等待固定当前角度，未采用示例环境慢转。制糖坊烟囱锚 `(158,46)`、产出锚 `(112,169)` 转为局部 `(30,-130)`、`(-16,-7)`；磨坊产出锚 `(117,168)` 即 `(-11,-8)`。其他静态加工设施使用工作中心附近 `(0,-8)`，收获 `(0,-10)`。蒸汽只在真实加工中新增，等待停止新增，现存短效果按视觉生命周期结束。

包内 `building_visual.gd.show_completion` 示例位置和清单产出锚不同；正式实现使用上述清单锚。FX 留在所属实例的空间关系中，没有全局高 Z。浮动数量采用真实结果的 `+数量`，默认数字字体 13px，背景按文字实际宽度绘制，不使用示例固定值。

## 三作物九素材与固定根点

实际运行植株为 `assets/plants/padded/{wheat|sugarcane|radish}_growing_{01|02|03}.png`；相同文件名在 `assets/plants/` 中为未 padding 原株，不能混用两种偏移。

| 作物 | 原株尺寸（三档） | 原株 root_px（三档） | 固定区及风参数依据 |
| --- | --- | --- | --- |
| 小麦 | 12×13 / 21×28 / 24×42 | (5,12) / (10,27) / (12,41) | 清单建议固定根上 1px，振幅 0.35/0.65/0.9px；示例脚本固定 2px |
| 甘蔗 | 13×16 / 26×33 / 32×53 | (6,15) / (13,32) / (17,52) | 清单建议固定根上 1px，振幅 0.4/0.9/1.3px；示例脚本固定 2px |
| 萝卜 | 12×10 / 20×21 / 28×30 | (6,9) / (9,19) / (14,29) | 固定根上 1/7/12px，包含萝卜根部，振幅 0.25/0.5/0.65px |

数字以 `docs/rooted_crop_manifest.json` 为可追溯入口；清单与独立示例的幅度/固定区不同。正式采用上表的逐档清单幅度，小麦/甘蔗根上固定 1px，萝卜固定 1/7/12px；不采用示例整株幅度。padding 左右各 4px，左上偏移为 `fieldRoot - sourceRoot - (4,0)`，不按 padded 图尺寸重新估根。

每田 15 个 q0 根点由 `u∈{-1,0,1}`、`v∈{-1,-0.5,0,0.5,1}` 得到田图坐标 `(112+32(u-v),128+16(u+v))`；换到田中心局部坐标时再减 `(112,128)`。按根 Y、再 X 保持稳定顺序。地面、根父节点、田容器不摇晃；只让叶片/茎上部按高度加权横向采样，萝卜根部不动。零振幅应还原原单株，阶段切换不改根位置、土地及接触阴影，不通过缩放上一档生成下一档。

实际 15 株各自进入与人物、设施同一接触点排序，位置为“田工作中心+单株局部根”。农田 Motion/Art 启用嵌套 YSort，各株原点位于根且 Z=0；建筑 Motion/Art 不启用内部 YSort，以节点树保持 body→sails→hub。背后株在人物后，前方株在人物前，土始终在实体之下。同田 15 株共享一份逐档材质，每帧只更新一次时间。

静态整田变为逐株，人物前后关系会更细，这不是保证完全相同的切换。小麦/甘蔗末档零振幅有包合同的重组对照，其他档及萝卜不应宣称与静态整田逐像素完全重合；必须逐档截图比较根、密度、重叠和人物穿行。其他四作物尚为整田静态，无法凭单张 PNG 获得逐株穿插；保持实例接触点排序，并如实记录此素材能力差异。

## FX、结果与时钟

共享 FX 实际类型为 water、seed_dust、steam、harvest、product_count；`water_drop.png`、`seed_grain.png`、`wood_chip.png` 在 `shared_fx/textures/`。harvest/steam 在示例中是程序绘制，不存在必须播放的收获 PNG 图集。已确认播种/浇水使用 NPC 合成帧，故本 Module 不再生成 water/seed_dust 作业 FX；不能照抄 `crop_field.gd.show_action("water")` 的 `set_wet(true)`，它会让表现擅自修改水分外观。

收获 FX 只消费该实例真实成功结果，产出量使用结果数量；加工同理。首次加载快照、反复刷新、相机返回、暂停恢复都不重放历史结果。同秒收获复种或完工续批仍消费一次完成结果；这些已完成产出不因自然进入下一轮而失效。拆除重建不能继承原实例的在播效果，未开始的旧经营秒结果不再启动。开发高倍率只保留最新有效可见结果，不以长队列在低倍率时补播。屏幕外省略及粒子预算只影响显示，不能少结算、多结算或改变经营顺序。

视觉时间跟随主游戏暂停和倍率；材质/叶片/FX 不读系统时钟，不创建第二个 `SimulationDriver`。同一 Module 内程序绘制：每实例仅一个最新产出，持续 0.85 视觉秒，收获 8 颗粒；糖坊每 0.38 视觉秒最多新增一团、最多 4 团，每团 1.5 视觉秒。高倍率只发最新团，不补发历史；未采用示例全局 40 效果或 0.6 秒合并。shader 保留双正弦与高度权重整数像素偏移，越界明确透明，时间按双波共同周期回绕以避免长时浮点精度下降。

## 必须完成的验收清单

1. 1080P 主场景录制磨坊等待冻结/加工转动、糖坊加工/等待：无双叶片，轮毂固定，蒸汽不从错误锚发出。
2. 三作物三档、干湿土地、零风/最大采用风：15 根固定、萝卜根固定、边缘不裁切涂抹，阶段切换无漂移。注意包 shader 当前直接采样，若修改幅度超出 padding 须显式保证越界透明，不靠边缘 clamp。
3. 工人穿过田后方、中间、前方，穿过相邻建筑/树木；地面不遮人、前株能遮脚、局部轮毂不穿越前景实体。
4. 完成同秒续批、重复同步、暂停恢复、目标拆除、改种、高倍率、大量屏幕外生产：FX 单次或按已确认规则省略，经营结果完全一致。
5. 满地图负载和有窗口平均至少 60 FPS、P95 帧间隔≤16.67ms，保存前后截图；失败须调整可见实例工作量或素材实现，不修改经营产量来降低负载。

`tests/integration/test_facility_motion.tscn` / `TestFacilityMotion.RunChecksAsync` 检查真实 Godot 节点的局部分层、等待冻结、加工叶片、三作物九档 15 根、萝卜固定区、重复配置、暂停、重复结果、清除不复活及高倍率淘汰。图形断言从实际截图直接取样：分别与来源塔身、叶片盖塔身、轮毂盖叶片、制糖坊底图的不透明颜色对照；旧静态叶片中当前三层均透明的像素必须显示背景，证实没有双份叶片。九档各取 q0 最前株可见固定区的来源不透明像素，推进前后均保持同位置同颜色，萝卜包含完整 1/7/12px 固定区。各项设置最少样本数，避免空采样误通过，不构建整套参考渲染器。

图形运行保存 `coverage/facility-motion-stages.png`、`facility-motion-output.png`、`facility-motion-harvest.png`；headless 不假称像素验收。经营同秒收获复种、完工续批及失效由 `TestProductionResults` 覆盖，真实地图路由、人物穿插、性能、覆盖率和导出由主任务统一完成。世界表现公共倍率为 0.5×/1×/2×，开发高倍率与人物一致按 1×播放视觉片段，仍只启动最新有效结果；不将开发经营倍率乘到视觉寿命以跳空所有反馈。
