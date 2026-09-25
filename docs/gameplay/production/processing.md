# 原料加工

每种原料只能由[作物表](crop-growth.md)中对应的场地加工；加工场地的建造、移除及加工免费。收获的原料先进入该作物的公共库存，空闲的匹配场地取走一份后开始加工，到期产出一份对应加工品。新建场地时若已有匹配原料，立即开始加工。

不同作物的加工时间见[作物表](crop-growth.md)。移除正在加工的场地时，已投入原料消失，不返还库存。原料不可直接出售，加工品出售规则见[出售与价格](../trading/sales.md)。

场地分配与完成时机由[FarmGame 的 tick 实现](../../architecture/game-state/farm-game/implementation-tick-order.md)负责；tests/unit/TestFarmGame.cs 检查匹配、移除和切换后的结果。
