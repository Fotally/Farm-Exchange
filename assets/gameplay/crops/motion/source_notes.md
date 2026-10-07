# 根点植株动效来源

关联 #108。来源为用户提供 `FarmExchange-Bright-Complete-v2.zip`，内部版本 2.0.0，ZIP SHA-256 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。

九张 PNG 从 `FarmExchange-Bright-Complete-v2/default/motion/assets/plants/padded/{wheat|sugarcane|radish}_growing_{01|02|03}.png` 按原名逐字节提取到本目录。左右各 4px padding、原始画布和 alpha 未改；没有放大、裁剪或从上一阶段缩放生成下一阶段。nearest、默认无 mipmaps。

`FacilityMotion` 使用 `docs/rooted_crop_manifest.json` 的 15 个 q0 根点及逐株 root_px；小麦/甘蔗根上 1px 固定，萝卜三档固定根上 1/7/12px。实际幅度分别为小麦 0.35/0.65/0.9px、甘蔗 0.4/0.9/1.3px、萝卜 0.25/0.5/0.65px。shader 来源为 `default/motion/shaders/rooted_wind.gdshader`，项目改写在 `scripts/world/rooted_wind.gdshader`：时间由主游戏输入，按清单固定区取参数，越界采样透明，保留双正弦风和按高度权重的整数像素偏移。

来源包 `docs/SOURCES_AND_LICENSES.md` 记录植株来自本项目既有 OpenAI imagegen 基稿及来源模块重组；授权范围未扩展。参考脚本、构建工具和未 padding 原株没有进入正式资源。许可总览见 [素材来源](../../../../docs/project/asset-sources.md)，根点、层级及与整田原图的差异见 [局部动效实现](../../../../docs/architecture/world/world-map/implementation-bright-motion.md)。
