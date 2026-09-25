---
paths:
  - "scenes/**"
---

# Godot 场景命名

- 场景与资源文件使用小写 snake_case，例如 main.tscn；节点名使用 PascalCase，与 Godot 节点类型和现有场景保持一致。
- 移动 C# 脚本或资源时同步更新 res:// 引用，使用项目指定引擎验证场景加载和导出。
