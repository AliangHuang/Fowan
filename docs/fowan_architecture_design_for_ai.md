# Fowan 开源客户端架构设计

> 面向人类开发者与 AI Coding Assistant
>
> 仓库：公开的 Fowan 原生客户端
>
> 详细边界：`docs/repository_boundaries.md`

## 1. 架构摘要

Fowan 使用“开源原生客户端 + 独立闭源核心”的长期架构，但当前版本尚未接入闭源核心。

本仓库负责可公开审查和贡献的客户端体验：

- Windows 原生 UI、窗口生命周期和平台集成。
- Toolbox、Todo、Diary 等现有工具的完整普通功能。
- 工具本地 JSON 数据、附件、查询、定位和天气集成。
- 设计资源、公开文档、安装器和开发脚本。

私有 FowanCore 只负责未来需要保护的能力：

- AI 工作流编排与模型策略。
- 用户信息加密、解密和密钥管理。
- 敏感索引、访问控制、授权和商业策略。

## 2. 当前公开客户端结构

```text
apps/
  windows/               # Toolbox shell
  windows-todo/          # Todo main window
  windows-todo-shared/   # Open-source Todo models, storage, and services
  windows-todo-sticky/   # Todo sticky window
  windows-diary/         # Diary window
  windows-diary-shared/  # Open-source Diary models, storage, and services
assets/                  # Brand and design assets
docs/                    # Public client, product, and boundary documents
scripts/                 # Build, run, package, and visual QA helpers
tests/                   # Open-source Shared tests and visual fixtures
```

`Shared` 表示同一工具的多个窗口共享代码。它不是闭源核心层，也不应仅因为代码包含模型、存储或查询就迁入 FowanCore。

## 3. 客户端分层

### 3.1 原生 UI

负责窗口、布局、主题、输入、可访问性、平台权限入口和用户可见错误。UI 可以持有临时视图状态，但不应复制未来私有核心的 AI、加密或商业策略。

### 3.2 工具 Shared 项目

负责现有工具的普通共享行为，包括：

- 公开模型与设置对象。
- 本地 JSON 数据路径、兼容读取和写入。
- Todo 查询、树结构、筛选和回收站。
- Diary 日记本、标签、附件、搜索、定位和天气。

这些代码保持开源，并由对应 Shared 测试覆盖。

### 3.3 平台服务

负责 Windows 自启动、托盘、通知、更新、文件选择、定位权限和进程生命周期。平台服务不得成为 AI 策略、密钥或敏感商业规则的隐藏落点。

## 4. 未来核心接入原则

在首个真实 AI 或加密用例出现前，不创建协议项目、SDK、sidecar 或占位 RPC。

需要接入 FowanCore 时遵循以下顺序：

1. 明确用户场景、数据边界和威胁模型。
2. 在公开仓库定义最小协议，只暴露稳定 request、response、error 和必要 event。
3. 为协议定义独立版本与 capability，避免客户端依赖私有内部模型。
4. 在 FowanCore 实现 AI、加密或敏感策略。
5. 增加兼容性、故障降级、安全和许可证测试后再进入发行包。

协议应描述客户端需要的结果，不应暴露提示模板、模型路由、密钥材料、内部索引结构或商业判定算法。

## 5. 数据与安全边界

- Todo 数据继续位于 `%LOCALAPPDATA%\Fowan\Todo`。
- Diary 数据继续位于 `%LOCALAPPDATA%\Fowan\Diary`。
- 当前数据位置和 schema 不因仓库拆分发生变化。
- 普通本地数据读写仍属于公开工具实现。
- 未来用户信息加解密、密钥生成、保存、轮换和恢复策略进入 FowanCore。
- 不得在日志、测试夹具、协议样例或崩溃报告中放入真实用户内容、提示、凭据或密钥。

## 6. 开发规则

- 修改现有工具时，优先在对应 UI 或 Shared 项目完成，不要把“业务逻辑”一概等同于闭源核心。
- 只有 AI 策略、用户信息加解密、密钥、敏感索引、授权或商业策略进入 FowanCore。
- 新增跨仓库能力前先更新 `docs/repository_boundaries.md`。
- 不允许公开客户端直接依赖未来 FowanCore 的内部 Rust crate、存储结构或私有模型。
- 所有构建、测试、XAML、恢复、分析器和脚本输出必须达到零警告、零错误。

## 7. 许可证边界

Fowan 当前以 GPL-3.0 发布。本次拆分不改变许可，也不把闭源二进制加入当前构建。

未来首次共同分发闭源核心前，必须专项评估 GPL 兼容性。进程隔离或 IPC 只是技术边界，不能替代对通信语义和组合分发方式的法律判断。
