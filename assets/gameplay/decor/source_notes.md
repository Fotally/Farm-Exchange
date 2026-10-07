# 清亮 v2 环境装饰来源

已按 #109 提取全部32类的固定q0，运行入口为WorldMap内部EnvironmentDecorations。来源 `assets/FarmExchange-Bright-Complete-v2.zip`，内部2.0.0，ZIP SHA-256 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。权威清单是包根 `FarmExchange-Bright-Complete-v2/default/static/manifest.json` 的group=decor。

包内 `default/static/decor/<id>/<id>_q0.png` 原字节复制到 `assets/gameplay/decor/<id>_q0.png`。全部32类名称、canvas、pivot、自然或人工布置方式见 [环境实现](../../../docs/architecture/world/world-map/implementation-environment-decoration.md)。只保留q0，其他方向和来源工程仍在原包；不裁透明边、不缩放、无像素重绘，nearest与关闭mipmaps沿世界纹理默认约定。

树石草花等采用原包静态生成及程序化资源，授权来源以包内 `docs/SOURCES_AND_LICENSES.md` 为准；不引用NPC第三方许可覆盖装饰。原包继续本地保留，各导出预设明确排除原ZIP和示例。环境不进入正式占用、金币、库存或工人任务；仅在实际建筑占用根点后永久清除，拆除不复生。
