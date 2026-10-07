# 生产短效果来源

关联 #108。当前共享效果是程序绘制，没有新增 PNG、独立时钟或示例场景。实现位于 `scripts/world/FacilityMotion.cs`，采用用户提供 `FarmExchange-Bright-Complete-v2.zip` 内部版本 2.0.0 的 `default/motion/shared_fx/fx_burst.gd` 中 harvest、steam、product_count 的像素矩形与浮动数字样式。原包 SHA-256 为 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。

实际改动：经营成功结果提供数量和类型；每实例仅保留一个最新短结果，持续 0.85 视觉秒；收获采用 8 个短颗粒；蒸汽仅真实加工时每 0.38 视觉秒新增一次，最多 4 团，1.5 视觉秒结束，高倍率不补发被跨过的历史团。字体使用工程 Godot 默认数字字体，没有复制示例字体；数量背景按实际文字宽度绘制。每实例效果与该实例参与共同深度关系，不放全场最高层。

播种/浇水使用人物合成帧，因此未采用 water、seed_dust、water_drop、seed_grain、wood_chip；没有复制示例程序改变作物水分的逻辑。蒸汽及收获没有来源 PNG 图集，不能把本目录没有图片解释为缺素材。

来源包 `docs/SOURCES_AND_LICENSES.md` 的程序生成部件记录适用，不扩大来源授权。许可总览见 [素材来源](../../../docs/project/asset-sources.md)，运行及验收说明见 [局部动效实现](../../../docs/architecture/world/world-map/implementation-bright-motion.md)。
