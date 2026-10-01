# 窗口尺寸、地图缩放与成熟作品的显示策略

- 关联 issue：[#76](https://github.com/Fotally/Farm-Exchange/issues/76)。
- 调研日期：2026-10-02（北京时间）。
- 用途：为全屏显示方式的设计讨论提供证据，不修改游戏代码、项目设置或生效玩法规则。
- 当前讨论倾向：全屏显示更多地图，农田保持原来的大小；具体镜头范围、界面比例和高 DPI 策略尚未确认。

## 结论与比较条件

查到的成熟作品并不统一采用“窗口越大，整个游戏画面同比例放大”。OpenRCT2 在固定整体比例和镜头档位时扩展地图视野；OpenTTD 在手动固定界面比例时也如此，但 Auto 界面比例可以联动地图档位；Factorio 明确区分固定对象比例与按窗口计算的可见距离限制。支持滚轮缩放不能证明窗口改变时采用哪条路径。

本报告只比较**同一显示器、同一系统 DPI，镜头及手动比例设置不变，游戏窗口可用区域由 1280×720 变为 1920×1080**。在这些条件下，“保持大小”指对象保持屏幕像素大小。换高 DPI 显示器后，同样的像素数不保证相同物理大小；全屏输出低分辨率再由显示器或系统放大，也不能与上述比较混为一谈。

| 作品与核对版本 | 窗口扩大时地图怎样变化 | 玩家缩放与界面比例 | 结论级别和边界 |
| --- | --- | --- | --- |
| OpenRCT2，官方源码提交 `2b91ae8641e1d866d23f89c3c999bb6dfd251de3`（2026-09-30 UTC） | 固定 `windowScale` 和地图 `zoom` 时，主视口增加宽高，显示更多地图，对象大小保持 | 地图镜头档位与整体 `windowScale` 是不同设置；后者作用于游戏画布，包括界面 | 官方手册明确像素对应比例，源码推断 resize 行为；不能说其整体比例只缩放 UI |
| OpenTTD，官方源码提交 `9e5832e65f496c0374da845c182c17fbdbaec061`（2026-10-01 UTC） | 手动固定界面比例时，地图镜头档位保持，扩大可见范围 | 支持地图缩放；Auto 界面比例按窗口大小计算，跨档时会联动地图镜头 | 源码推断；必须说明手动/Auto，不能断言所有设置都保持像素大小 |
| Factorio，官方 Runtime API 页面标示 2.1.20；历史开发博文另标日期 | 固定 `zoom` 与对象观感尺寸对应；`distance` 限制则按窗口大小和长宽比计算倍率，在限制生效时可保持可见距离 | 地图倍率、可见距离限制、界面比例及屏幕密度是分开的概念 | 官方明确两种规格；固定倍率下扩大窗口可增加视野是接口定义推断；未运行客户端验证所有控制器的默认 resize 行为 |

前两项是官方开源项目当前提交的行为，不能冒充原版商业游戏全部版本的行为。Factorio 的当前 API 页面属于 2.1 系列；[开发者公告](https://www.factorio.com/blog/post/fff-444)记录 2.1 于 2026-06-26 进入 experimental，因此不能把本报告的 API 表述当作所有旧稳定版的默认参数。

## OpenRCT2：窗口尺寸与整体比例分别决定画布尺寸

[官方 Display 手册](https://docs.openrct2.io/en/latest/setup/options.html#display)明确：整体比例为 1.00 时，一个游戏像素对应一个屏幕像素；整体比例可用于改善高分辨率下界面过小的问题。该页标示文档版本 0.4.0，下面另以当前源码核对窗口变化链。

当前源码的 [UiContext::OnResize](https://github.com/OpenRCT2/OpenRCT2/blob/2b91ae8641e1d866d23f89c3c999bb6dfd251de3/src/openrct2-ui/UiContext.cpp#L847-L860)把窗口宽高分别除以固定的 `windowScale`，得到游戏画布宽高。[WindowResizeGuiMainToolbars](https://github.com/OpenRCT2/OpenRCT2/blob/2b91ae8641e1d866d23f89c3c999bb6dfd251de3/src/openrct2/interface/Window.cpp#L725-L739)同步主视口宽高，并未修改地图 `zoom`。[Viewport 的 ViewWidth/ViewHeight](https://github.com/OpenRCT2/OpenRCT2/blob/2b91ae8641e1d866d23f89c3c999bb6dfd251de3/src/openrct2/interface/Viewport.h#L38-L57)再用地图缩放档位计算可见世界范围。

由这条链推断：保持两项比例不变，窗口长宽增加 50%，可见世界范围的长宽也增加约 50%，对象的屏幕像素大小保持。反过来，主动提高 `windowScale` 会整体放大画布呈现；这不是拖大窗口自动触发的同一种操作。源码还在[初始化时推断显示密度](https://github.com/OpenRCT2/OpenRCT2/blob/2b91ae8641e1d866d23f89c3c999bb6dfd251de3/src/openrct2-ui/UiContext.cpp#L773-L788)，所以高 DPI 初始化需要单独看待。

## OpenTTD：固定比例扩大视野，Auto 可以改变地图档位

窗口尺寸变化后，[GameSizeChanged](https://github.com/OpenTTD/OpenTTD/blob/9e5832e65f496c0374da845c182c17fbdbaec061/src/main_gui.cpp#L592-L599)先更新画面，再重新布置窗口。[主窗口 OnResize](https://github.com/OpenTTD/OpenTTD/blob/9e5832e65f496c0374da845c182c17fbdbaec061/src/main_gui.cpp#L449-L455)调用 [UpdateViewportCoordinates](https://github.com/OpenTTD/OpenTTD/blob/9e5832e65f496c0374da845c182c17fbdbaec061/src/widget.cpp#L2451-L2463)，使用当前 `zoom` 从新像素宽高计算可见世界范围；[ScaleByZoom](https://github.com/OpenTTD/OpenTTD/blob/9e5832e65f496c0374da845c182c17fbdbaec061/src/zoom_func.h#L22-L25)只取像素数和镜头档位。因此在固定比例、固定档位条件下，窗口增加像素会扩大视野。

不过不能只读主窗口 resize 就停止。[ScreenSizeChanged](https://github.com/OpenTTD/OpenTTD/blob/9e5832e65f496c0374da845c182c17fbdbaec061/src/gfx.cpp#L1321-L1337)会检查界面比例，[UpdateGUIZoom](https://github.com/OpenTTD/OpenTTD/blob/9e5832e65f496c0374da845c182c17fbdbaec061/src/gfx.cpp#L1807-L1826)在 Auto 时按窗口宽/640、高/480 中较小者计算比例，并映射到离散图像档位；若该档位变化，[AdjustGUIZoom](https://github.com/OpenTTD/OpenTTD/blob/9e5832e65f496c0374da845c182c17fbdbaec061/src/gfx.cpp#L1859-L1869)会调整视口 `zoom`。

所以 OpenTTD 同时展示了两种处理：手动固定比例时扩大窗口增加视野；Auto 设置下跨档可以同时放大对象。后者是按条件改变离散镜头档位，并非将固定基准画布持续同比例拉伸，不能视为与本项目当前方式完全相同。本次未核验发行版的初始配置，不宣称 Auto 是所有玩家的默认设置。

## Factorio：固定倍率与可见距离限制不能混为一谈

[ZoomSpecification 官方定义](https://lua-api.factorio.com/latest/concepts/ZoomSpecification.html)明确两种互斥规格：`zoom` 是固定倍率，与世界对象的感知尺寸对应；`distance` 则按窗口尺寸与长宽比动态计算倍率，用于限制玩家可见范围。[ZoomLimits](https://lua-api.factorio.com/latest/concepts/ZoomLimits.html)将这些规格用于最近、最远和普通游戏视图边界，而不是把 `distance` 定义为所有时刻的镜头自动行为。

因此，**固定倍率**与**已经到达某个可见距离边界**必须分开讨论。在 16:9 的窗口中，若某一生效边界要求横向显示固定数量的格子，扩大窗口就需要相应增加对象的像素大小；这直接由官方 `distance` 定义得出。固定 `zoom` 的普通视图则不能仅因为存在距离边界，就被描述为始终随窗口整体放大。页面没有列出全部控制器的引擎默认边界，本次也未运行 Factorio 客户端测量，默认 resize 是否触发钳制仍需具体配置才能判断。

开发者在 [Friday Facts #204（2017-08-18）](https://www.factorio.com/blog/post/fff-204)把 2.0 倍称为高分辨率素材的像素精确倍率，并说明调整滚轮步长以准确到达该倍率。这支持其镜头具有像素对应含义，但不证明所有分辨率和当前默认设置的结果。[SpriteParameters](https://lua-api.factorio.com/latest/types/SpriteParameters.html)提到普通素材常用每格 32 像素，同时明确该素材尺度不是强制值；它不是“任何显示环境下每格必为 32 屏幕像素”的保证。本次未找到官方将 `LuaPlayer.zoom` 统一定义为按 1080p 归一化的陈述，故不采用该说法，也不把缺少陈述当作反证。

界面比例另有证据：[Friday Facts #277（2019-01-11）](https://www.factorio.com/blog/post/fff-277)讨论不同 UI 比例下的控件像素尺寸；[LuaPlayer 的 display_density_scale](https://lua-api.factorio.com/latest/classes/LuaPlayer.html#display_density_scale)另记录由显示密度决定的比例因素。它们说明界面可读性和地图镜头可以分别讨论，不能直接从 UI 设置推导地图 resize 行为。

## 证据范围与对本项目的讨论启示

本次使用开发者手册、官方 API、开发者博文及固定提交的官方源码；未安装这些游戏进行前后截图测量。没有使用非授权反编译仓库。Stardew Valley、Terraria 未纳入确定比较，因为本次未取得足够的一手材料证明同窗口变化条件下的完整行为；社区 Wiki、玩家帖子及“有缩放滑块”不足以补上这个缺口。

这三个有针对性的样本不能用于统计整个行业哪种方式更常见。已经可以确认的是：**“全屏显示更多地图、对象保持大小”有成熟建造经营作品的实际依据；“窗口变化自动调整比例”也有配置相关的实际依据。** 成熟作品还会分别处理高 DPI、界面可读性与最远可见范围，因此两条显示路径都属于设计选择。

结合用户已表达的倾向，本项目下一轮可讨论：同显示器切换窗口/全屏时保持地图对象大小；滚轮仍由玩家控制远近；界面是否继续适应可用空间；高 DPI 下以像素大小还是逻辑大小为准；是否另设最远视野限制。以上是待确认的设计问题，不是本次获准实施的规则，也不把现有同比例放大行为定义为缺陷。
