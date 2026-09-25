# 开局、建造与购地

固定地图为 128×128 格。开局在中心额外解锁五块土地：农田位于 (63,63)、(64,63)、(65,63)，加工场地位于 (63,64)、(64,64)。这些建筑直接建好，不消耗免费选地次数；农田尚未播种，由工人按正常 tick 照料。

开局从六种作物中随机选主作物，以 50% 概率形成三块同种农田和两处同种加工场地；另有 50% 概率形成两块主作物农田、一块另一种作物农田，两处场地分别匹配这两种作物。同一市场种子生成同一开局配置，新游戏随机取种子。具体生成流程见[实现说明](../../architecture/game-state/farm-game/implementation-opening-layout.md)。

初始金币 50.00，另有两次任意位置免费解锁土地的机会。用完后每块土地花费 10.00 金币。已解锁空地可免费建农田或一种加工场地；移除建筑免费，土地保持已解锁。作物及加工中的物品移除后如何处理，分别见[作物](../production/crop-growth.md)和[加工](../production/processing.md)。

购地和建造命令见[FarmGame 接口](../../architecture/game-state/farm-game/interface-farm-game.md)；tests/unit/TestFarmGame.cs 检查开局和购地。
