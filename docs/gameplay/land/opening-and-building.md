# 开局与建筑建造

地图固定为 128×128 格。开局中心预置三块农田，位于 (63,63)、(64,63)、(65,63)；两处加工场地位于 (63,64)、(64,64)。这些建筑直接建好，不扣金币。农田尚未播种，由工人按经营节奏照料。

开局从六种作物中随机选主作物，以 50% 概率形成三块同种农田和两处同种加工场地；另有 50% 概率形成两块主作物农田、一块另一种作物农田，两处场地分别匹配这两种作物。同一市场种子生成同一开局配置，新游戏随机取种子。具体生成流程见[实现说明](../../architecture/game-state/farm-game/implementation-opening-layout.md)。

初始金币为 50.00。地图中的空地均可直接建造农田或对应作物的加工场地，无土地解锁步骤。每座新建筑暂收 10.00 金币；余额不足或地块已有建筑时不能建造，也不扣费。农田建成时默认种小麦，可在农田详情中改种；加工场地在建造目录中选择具体品种。移除建筑不退费，再建仍需支付建造费。移除时作物与加工中物品的处理分别见[作物](../production/crop-growth.md)和[加工](../production/processing.md)。

建造和移除命令见[FarmGame 接口](../../architecture/game-state/farm-game/interface-farm-game.md)，玩家操作见[主界面接口](../../architecture/ui/main/interface-main.md)。`tests/unit/TestFarmGame.cs` 验证开局、收费与余额限制；`tests/e2e/TestCoreLoop.cs` 验证目录和摆放流程。
