# Fowan AI Configuration Changelog

## Unreleased - 2026-07-13

- 新增独立 `Fowan.Ai.Config.Windows.exe`，管理渠道、多个密钥、模型和 `ai.chat` 默认绑定。
- 支持 `--page=credentials|models|bindings` 定位，并可启动或激活独立 AI 对话应用。
- API 密钥仅交给 Fowan Core 的 Windows Credential Manager 安全存储，不进入客户端数据库或日志。
