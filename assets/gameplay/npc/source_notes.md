# 清亮人物运行素材来源

当前本地接入 #107：FarmExchange-Bright-Complete-v2.zip，内部版本 2.0.0，ZIP SHA-256 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。权威目录 default/npcs/catalog.json，逐角色逐帧约定 default/npcs/characters/npcNNN/animations.json。

来源 `default/npcs/characters/npcNNN/npcNNN_{idle,walk,run,sow,water}.png` 原字节复制至本目录 npcNNN/。来源编号保留 001、002、005、006、007、009～023，共 20 位，每人五张合成图。没有接入 carry_idle、carry_walk、harvest、process，也未复制身体/道具层或共享撒种水滴，避免合成内容重复。

npcNNN_sprite_frames.tres 按来源 animations.json 重新生成，引用改为项目绝对资源路径，仅收录已采用的 20 个四向片段。每帧 64×64，四行 down/left/up/right，脚根 `(32,60)`；居中 Sprite 偏移 `(0,-28)`。右向已烘焙镜像，运行不再 FlipH。帧数、透明边和逐帧秒数保留：idle/sow/water 八帧，walk/run 六帧。SpriteFrames 速度为 1、帧 duration 为来源秒数；播种浇水由循环改为单次，供真实成功结果消费，其他片段循环。此重建资源不是包内 .tres 的逐字节副本。

PNG 使用 nearest（角色节点显式设置）、mipmaps 关闭；首次 Godot 导入使用像素图默认无 mipmaps。主场景使用 npc001、npc002、npc005，独立预览可切换全部 20 位。旧 npc_animation_NNN.png 保留历史文件，但角色场景与新运行脚本不再引用，不作为加载兜底。

授权记录：来源包原先将NPC原图与派生资源列为私人评估，具体第三方许可证未核实。2026-10-07，项目维护者明确允许公开所有已提供素材，包含本次100张NPC派生PNG与20个SpriteFrames，可纳入公开仓库及游戏分发。本条记录本次提交与分发授权，来源包的原始许可说明继续保留。总览见[素材来源](../../../docs/project/asset-sources.md)。
