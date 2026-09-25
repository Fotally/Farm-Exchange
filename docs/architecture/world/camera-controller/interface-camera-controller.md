# CameraController 对外接口

对应类型：FarmExchange.World.CameraController，代码位于 scripts/world/CameraController.cs。作为 Godot Camera2D 节点，它接收输入，使用 WorldMap.SelectAtScreenPosition 完成短按选格，使用 WorldMap.ClampCameraCenter 限制平移。

左键按下到释放的移动距离达到 8 像素时转为拖动，不再发出选格；释放立即结束拖动，即使释放事件被界面拦截，也在下一帧校正。中键拖动、滚轮缩放、WASD 和方向键移动由该模块处理。缩放范围 1.25～2 倍。

它不计算等距坐标、不修改经营状态；输入的玩家可见行为见[地图与操作](../../../gameplay/world/map-and-camera.md)。tests/integration/TestCameraInteraction.cs 检查镜头与地图协作。
