# 素材来源记录

本页记录已纳入仓库的第三方素材。素材加入不代表已接入经营或画面；实际使用入口以对应场景和架构文档为准。

## FrameRonin 免费角色动画图

- **适用文件**：`assets/gameplay/npc/npc_animation_*.png`，共 20 张；保留来源编号 001、002、005～007、009～023。各文件只从用户提供的同编号 `character_animation_*.png` 复制并重命名，没有修改像素。
- **来源**：[FrameRonin 免费角色素材页](https://frameronin.com/free-character-library/)；用户提供的下载包说明为“古代三国角色包 · Godot 4 可运行示例”。
- **规格**：原包说明每帧为 64×64 像素，每行动画 6 帧；第 8、9、10 行分别作为下、左、上移动动画，向右时可镜像左移动。
- **授权记录**：来源页标示免费角色包并提供下载，当前页面与随包说明未写明具体许可证或再分发条款。2026-09-27，用户明确允许将这批素材放入下一次 PR。本记录不将“免费下载”解释为 CC0 或商业使用授权。
- **当前使用**：本工程的[可复用角色场景](../architecture/characters/npc-character/interface-npc-character.md)和[独立预览](../architecture/ui/npc-preview/interface-npc-preview.md)使用这些图集；主经营场景尚未引用。随包的独立 Godot 示例项目与 GDScript 未纳入仓库。
