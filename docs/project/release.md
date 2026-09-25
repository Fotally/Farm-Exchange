# 正式版本发布

正式版本只从已人工合并的 `main` 构建，采用 `v主版本.次版本.修订版本`。不兼容既有存档或核心玩法规则的变更递增主版本；兼容地新增玩法、系统或内容递增次版本；兼容地修复缺陷、优化性能或调整表现、文档和构建递增修订版本。首个稳定公开版本为 `v1.0.0`，此前使用 `v0.次版本.修订版本`。

Windows 正式包固定写入：

```text
build/releases/v主版本.次版本.修订版本/FarmExchange-v主版本.次版本.修订版本-windows-x86_64.zip
```

压缩包不纳入 Git，只上传到同版本 Git 标签对应的 GitHub Release。发布前须重新完成 Release 编译、全部自动化场景测试、Windows 导出和导出程序启动验收；标签、GitHub Release 与压缩包中的版本号一致才算发布完成。

[macOS CI](macos-build.md) 生成的 Universal 2 ZIP 是提交级验收产物；当前未配置 Apple 公证，不纳入本节的正式发布包。
