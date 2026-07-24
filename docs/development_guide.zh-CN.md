# Fowan 开发执行指南

规范版本：2026-07-14

英文 [`CONTRIBUTING.md`](../CONTRIBUTING.md) 是强制事实源；本文用于中文执行。

## 开发前

1. 阅读 `AGENTS.md`、仓库边界及目标组件架构文档。
2. 先检索已有 Workspace、Session、端口和共享能力，禁止复制相同业务状态。
3. 新工具、新项目、顶层功能模块、共享服务或平台适配器必须先从
   [`template.md`](design-proposals/template.md) 建立设计提案。
4. 提案状态变为 `accepted` 后，才能登记组件并开始实现。
5. 协议、跨仓库契约、数据迁移、安全边界、进程边界或难以撤销的依赖变化还必须
   新增或更新 ADR。

## 设计规则

- Workspace/Session 是唯一业务状态所有者。
- Window 只装配依赖、管理窗口生命周期和转发 UI 事件。
- Presentation 只读取不可变快照和展示状态，通过 typed command 发起修改。
- Application 不依赖 Presentation 或 Windows 实现；平台代码只实现端口。
- 成功用例先持久化再发布 typed change event；失败用例保持旧状态或返回明确失败。
- 禁止全局可变状态、service locator、通用事件总线和绕层静态调用。
- 架构质量按职责、状态所有权和依赖方向验收，不按代码行数验收。

## 推荐实施顺序

先写 Application/Domain 与失败测试，再写端口和适配器，最后接入 Presentation 与
Window。PR 必须填写设计提案、状态所有者、失败模型、兼容性和完整验证证据。

提交评审前运行：

```powershell
./scripts/verify.ps1
```

CI 会校验组件清单、提案状态、分层依赖、状态所有权、构建和测试。缺少 accepted
提案的新组件不能合并。

`baseline` 仅适用于 `docs/architecture-baseline.json` 已冻结的精确条目。新增组件、
模块或依赖不得自行声明为基线；修改冻结清单必须同时引用 accepted 提案和 ADR。
