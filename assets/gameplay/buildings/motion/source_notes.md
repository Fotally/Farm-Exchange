# 建筑局部动效来源

关联 #108。来源为用户提供 `FarmExchange-Bright-Complete-v2.zip`，内部版本 2.0.0，ZIP SHA-256 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。

四张 PNG 从 `FarmExchange-Bright-Complete-v2/default/motion/assets/buildings/` 按原名逐字节提取到本目录：`windmill_body.png`、`windmill_sails_256.png`、`windmill_hub.png`、`sugarworkshop_body.png`。没有重绘、裁切、缩放或修改透明边。运行使用 `FacilityMotion`；Godot 节点采用 nearest，默认无 mipmaps。

塔身和糖坊保留 256×240 画布及 pivot=(128,176)。叶片图按 16×16 排列的 256 帧，每帧 128×128，中心落在主体画布 (125,104)；轮毂与叶片同中心。局部次序为塔身→叶片→轮毂，三层相同 Z，由节点树顺序实现，不把轮毂抬到全地图前景。塔身替换原含叶片的静态整图，不重复绘制固定叶片。等待时保持叶片角度，加工时以 0.53 弧度/视觉秒推进；制糖坊只在实际加工时从烟囱锚 (158,46) 新增蒸汽。

来源包 `docs/SOURCES_AND_LICENSES.md` 记录建筑来自本项目既有 OpenAI imagegen 基稿，活动部件由来源模块制作；本记录不扩展包中授权。原包和示例工程未复制进正式资源。许可总览见 [素材来源](../../../../docs/project/asset-sources.md)，运行契约见 [局部动效实现](../../../../docs/architecture/world/world-map/implementation-bright-motion.md)。
