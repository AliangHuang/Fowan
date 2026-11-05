# ADR-0004: AI 凭据的基础与高级创建模式

## Status

Accepted.

## Context

首次添加 API Key 时，用户只需要提供配置名称、渠道和密钥；Base URL、模型与渠道特有
能力属于可选覆盖项。将默认模型和提供商策略放在 Windows 窗口中会使不同客户端得到不同
结果，也会让 API Key 的首次保存与模型创建分裂成多次操作。

## Decision

`ai.credentials.upsert` 在 `contractRevision=1` 中可选接收 `initialModelIds` 与
`thinkingEnabled`。配置窗口把这两项以及 Base URL 放在“高级模式（可选）”中；基础字段
始终必填。未填写高级值时，Core 解析渠道默认 Base URL，并在创建凭据的同一事务内创建
该渠道的预设模型。

当前仅支持 DeepSeek：默认创建 `deepseek-v4-flash`、`deepseek-v4-pro`，且二者的思考
模式默认启用。高级模式可选择初始模型集合并覆盖思考开关；空的模型选择仍表示采用默认
模型。Core 是这些默认值、模型持久化与请求参数的唯一所有者，Windows 只采集用户意图。

工具默认模型绑定可选保存 `thinkingEffort`。Core 在模型列表中返回每个模型实际支持的
`thinkingEffortOptions`，配置中心据此只显示合法档位；当前两个 DeepSeek V4 模型均为
`high` 与 `max`。未设置时不发送强度参数，保持模型默认行为。

## Compatibility and security

协议版本保持 `0.1`，现有握手修订保持不变；Core 的 SQLite 模式修订为 3。当前有效的
V1/V2 数据库会增加模型思考开关或绑定思考强度列并迁移，非受支持的旧模式仍要求重置。API Key 继续只由 Core 写入凭据存储，公共客户端、
协议示例和日志均不包含明文密钥。

## Verification

验证覆盖协议 schema/fixtures、默认与高级创建策略、V1/V2 到 V3 迁移、DeepSeek 请求的
思考参数及强度参数、Windows 共享测试、统一 Debug 运行目录以及配置窗口的基础/高级显示。
