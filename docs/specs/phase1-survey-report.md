# Flow Ring 第一步：勘察现状报告

> 阶段：Phase 1 / 7
> 日期：2026-10-02
> Agent 角色：Flow Ring 主 Agent（LangGPT 七段式骨架第 1 步执行）
> 适用设计稿：docs/architecture/v1.0-architecture.md（997 行）、technical-architecture-blueprint.md（2094 行）、addendum-v1.0.md（381 行）

---

## 1. 接口清单（Host ↔ WebView2 通信协议）

### 1.1 设计稿已定义的协议消息
来源：v1.0-architecture.md §4.5（C# ↔ TS 双向协议节选）

C# → TS（8 条消息）：
- `RING_OPEN` { originPoint, ringTree, deadZonePx }
- `RING_HIGHLIGHT` { direction }
- `RING_CLOSE` { reason }
- `PROFILE_LIST` { profiles[] }
- `RING_STUDIO_LOAD` { profile, ringGraph, actionLibrary }
- ……其余 3 条未在 §4.5 节选内列出（应来自 technical-architecture-blueprint.md 后续节）

TS → C#（7 条消息）：
- `RING_DIRECTION_LOCK` { direction }
- `RING_TRIGGER` { triggerType, modifierState }
- `ACTION_PREVIEW` { actionId }
- `STUDIO_SAVE` { profile, ringGraph }
- `FLOW_CODE_EXPORT` { profileId }
- `FLOW_CODE_IMPORT` { code }
- ……其余 1 条未在 §4.5 节选内列出

### 1.2 物理位置规划（设计稿）
- `src/RingProtocol/`：C# 端 Messages.cs + ts/src/messages.ts + ts/src/types.ts；含 `build.ts` 把 TS 编译产物导出给 RingUI
- `src/SharedSchema/schemas/`：JSON Schema（profile / ring-node / action / flow-code），通过 generate.ts 生成 C# 与 TS 两端类型
- 关键约束：§2.3 规则 4 强调"RingProtocol 是 C# 与 TS 的唯一共享定义——禁止在两侧手抄类型"

### 1.3 当前物理状态
- `docs/ring-protocol.md`：**未落盘**（应独立成文的协议 schema 文档）
- `src/RingProtocol/Messages.cs`：**不存在**
- `src/RingProtocol/ts/src/messages.ts`：**不存在**
- `src/SharedSchema/schemas/*.json`：**不存在**
- 协议消息的 payload 字段详细 schema（如 RING_OPEN.originPoint 的坐标类型、单位、坐标系）：**未在 §4.5 节选中展开，需查 technical-architecture-blueprint.md 第 9 节（Q1-Q5）**

### 1.4 缺口与降级
- 缺口：协议 schema 散落在两份蓝图 markdown 里，没有可被代码引用的 JSON Schema 或单一权威 markdown
- 降级方案：
  - Phase 2 搭脚手架时一并生成 `docs/ring-protocol.md`（消息清单 + payload schema + 版本号）
  - 同时落 `src/SharedSchema/schemas/profile.schema.json` 等四个 schema 文件
  - Phase 3 起实现 `src/RingProtocol/Messages.cs` 与 `ts/src/messages.ts`，从同一份 schema 生成

---

## 2. Profile JSON Schema 约束

### 2.1 设计稿已定义的 schema 片段
来源：v1.0-architecture.md §5.1-§5.3

- §5.1 Profile Schema：包含 metadata（id / name / version / schemaVersion / createdAt / updatedAt / checksum / tags）、ringGraph（rootId + nodes 字典）、actionRefs、contextRules；id 用 `^[a-z0-9-]{3,64}$` 正则，checksum 用 `^[a-f0-9]{64}$`（SHA-256），schemaVersion 用 `^\d+\.\d+$`
- §5.2 ActionDef Schema：含 ActionKind 枚举（Keyboard / System / Application / AI / Workflow），各种 payload（KeyboardPayload / SystemPayload / ApplicationPayload / AIPayload / WorkflowPayload）
- §5.3 FlowCode Schema：含加密元信息（algorithm: AES-256-GCM、kdf: PBKDF2-SHA256、iterations ≥ 100000、salt、nonce）
- §5.4 持久化目录布局：`%APPDATA%/FlowRing/storage/{profiles, actions, settings, snapshots, logs}/` + `cache/` + `state/`

### 2.2 当前物理状态
- `src/SharedSchema/schemas/profile.schema.json`：**不存在**
- `src/SharedSchema/schemas/ring-node.schema.json`：**不存在**
- `src/SharedSchema/schemas/action.schema.json`：**不存在**
- `src/SharedSchema/schemas/flow-code.schema.json`：**不存在**
- 设计稿 §5 给出的是 JSON Schema 片段（Markdown 代码块），不是可被 C#/TS 校验器直接读取的 .json 文件

### 2.3 缺口与降级
- 缺口：JSON Schema 没有独立落盘，导致：
  1. ProfileStore.SaveAsync() 无法做 schema 校验（目前只能"写入失败再恢复"）
  2. FlowCodeCodec 解码时缺少权威 schema
  3. C# 与 TS 两端类型无法用单一来源（generate.ts）派生
- 降级方案：Phase 2 搭脚手架时把 §5.1-§5.3 的 Markdown JSON Schema 抽到 `src/SharedSchema/schemas/` 下四个独立 .json 文件，作为权威定义

---

## 3. 已有代码与缺失模块

### 3.1 当前代码状态
- **代码总量：0 行**
- src/ 目录不存在
- tests/ 目录不存在
- packages/ 目录不存在
- 仅 docs/architecture/ 下三份 Markdown 设计稿（架构 + 技术 + 约束）
- 仓库未 git init

### 3.2 设计稿规划的 9 个模块（v1.0-architecture.md §2.2）
| 模块 | 物理位置规划 | 当前 | 缺口 |
|---|---|---|---|
| RingCore | src/RingCore/ | 空 | 整个模块待实现（Geometry / StateMachine / Ring / Action / Profile / FlowCode 子目录） |
| RingCore.Tests | tests/RingCore.Tests/ | 空 | 测试覆盖要求 > 90%（§7 Sprint Plan 验收口径） |
| DesktopBridge | src/DesktopBridge/ | 空 | IDesktopBridge + Win32 实现（Abstractions / Win32 / Native 子目录） |
| DesktopBridge.Tests | tests/DesktopBridge.Tests/ | 空 | 集成测试，需真实 Win32 环境 |
| DesktopHost | src/DesktopHost/ | 空 | Program.cs + Tray + Host（WebView2Host / BridgeServer / MessageRouter） |
| RingProtocol | src/RingProtocol/ | 空 | Messages.cs + ts 子项目 + build.ts |
| RingUI | src/RingUI/ | 空 | Vite + React 18 + TS，5 个页面（RuntimeRing / RingStudio / ProfileManager / FlowCode / Settings） |
| RingUI.Tests | tests/RingUI.Tests/ | 空 | Vitest + RTL |
| SharedSchema | src/SharedSchema/ | 空 | 4 个 JSON Schema + generate.ts |

### 3.3 缺口与降级
- 缺口：9 个模块全空白，需要从 Phase 2 起逐步填充
- 降级方案：
  - Phase 2 一次性把 9 个项目的目录骨架 + csproj/package.json/.editorconfig/.gitignore 创建出来，确保 dotnet build + pnpm build 双空跑通过
  - Phase 3 填 DesktopBridge + DesktopHost（含 Win32 P/Invoke、托盘、WebView2 宿主、Named Pipe 服务端）
  - Phase 4 填 RingCore 纯逻辑（含 SpatialEvent / InputStateMachine / DirectionResolver / RingNode 图 / ProfileResolver / ContextEngine）
  - Phase 5 填 RingUI 四页（RuntimeRing 仅作占位 + Studio 三栏 + Manager 列表 + Flow Code 导出导入 + Settings）
  - Phase 6 填 ActionEngine（Capability-based + Pipeline + Registry + 三级 Permission Tier）+ ProfileStore（分文件 JSON + 原子写 + 快照）+ FlowCodeCodec（Snapshot + JSON Patch + Vector Clock + AES-256-GCM）
  - Phase 7 补齐测试矩阵（RingCore > 90% / ActionEngine > 80%）+ 性能 benchmark + 三条异常路径（WebView2 Runtime 缺失 / WH_MOUSE_LL Hook 丢失 / 写操作失败回滚）+ dotnet publish 打包

---

## 4. 风险清单

### 4.1 工具链风险
- **R1 - 中文路径在 Git Bash 下的引号与空格处理**：flow-ring/ 位于 `D:\PC\Documents\新建文件夹 (2)\.zcode\workspace\default\flow-ring\`，含中文与括号。本会话已实测 `cd "/d/PC/Documents/新建文件夹 (2)/flow-ring"` 在某些 bash 上下文会失败，必须用绝对路径 + `\(` `\)` 转义，或 cd 进父目录再 `cd flow-ring/`。**已确认当前可用路径：`/d/PC/Documents/新建文件夹 (2)/.zcode/workspace/default/flow-ring/`**
- **R2 - 全局 HTTP/HTTPS 代理当前 shell 未加载**：当前会话 env 中 HTTP_PROXY / HTTPS_PROXY 为空。git push 走 GitHub 时按 memory 须走 127.0.0.1:7890；每次需临时注入 `HTTP_PROXY=http://127.0.0.1:7890 HTTPS_PROXY=http://127.0.0.1:7890 git push`，或 `git config --global http.proxy http://127.0.0.1:7890`
- **R3 - GitHub MCP 工具不需本机代理**：mcp__github__* 系列调用走 MCP server 通道，无需本机代理变量；本地 git 操作才需要

### 4.2 架构风险
- **R4 - §4.5 RingProtocol 节选只列了消息名不含 payload 完整 schema**：需在 Phase 2 补 docs/ring-protocol.md 时把每个消息的 payload 类型、字段、单位、坐标系、版本号钉死
- **R5 - §5.1 Profile Schema 在 technical-architecture-blueprint.md Q1-Q10 中可能进一步收紧正则或字段**：待 Phase 2 通读技术蓝图第 9 节后再定稿
- **R6 - §7 Sprint Plan 在 v1.0 与本蓝图中的 MCP 口径不完全一致**：v1.0 §7.1 切 6 个 Sprint × 2 周 = 12 周；本骨架切 7 个阶段。需在 Phase 2 文档中说明阶段 ≠ Sprint，是更高层的工作流
- **R7 - §6.2 时序预算 vs 本骨架 P95 200ms 触发 / 50ms 执行**：骨架的 Done Criteria 第 5 条只列了两项（按下→Ring 显示 200ms / 释放→执行 50ms），但设计稿 §6.2 有 10 个细粒度时序；验收时按本骨架的两项做硬约束，其他为设计稿软约束

### 4.3 实施风险
- **R8 - src/SharedSchema generate.ts 实现成本未评估**：JSON Schema → C# 类型 + JSON Schema → TS 类型，需要选择工具链（NSwag / QuickType / 自写 Roslyn Source Generator / 自写 ts-json-schema-generator）。需在 Phase 2 起步时先做工具选型 spike
- **R9 - WebView2 Runtime 在 Windows 10 早期版本缺失**：Done Criteria 第 7 条要求 Bootstrapper 自动装 + 弹窗不可装；Microsoft.Bootstrapper 包需联网，且国内网络可能不稳
- **R10 - WH_MOUSE_LL Hook 丢失场景的检测时机**：Done Criteria 第 7 条要求 Hook 丢失时暂停所有 Action 触发 + 通知浮窗。需在 Phase 3 实现时明确"丢失"的判定（连续 N 次 SetWindowsHookEx 失败？或 hook proc 长时间未被调用？）
- **R11 - Profile 原子写的快照保留 N=5 个的成本**：每次保存生成一个快照文件，长期使用会持续增长。需 Phase 6 设计 LRU 或按时间窗口清理策略

### 4.4 验收风险
- **R12 - SendInput 真实执行成功的人工验证**：Done Criteria 第 4 条要求"一次完整 sendinput 执行成功"，需在 Phase 7 自检报告中由 Agent 实测或在隔离 VM 中跑通
- **R13 - 性能 P95 200ms / 50ms 在 Windows 10 vs 11 上差异**：WebView2 冷启动时间在不同 Windows 版本下差异显著，benchmark 报告需注明 Windows 版本号

---

## 5. 第一步输出物

- 本报告：docs/specs/phase1-survey-report.md
- 关联蓝图（已在 docs/architecture/ 下，不在 Phase 1 新增）：
  - v1.0-architecture.md（8 节 + ADR + 附录）
  - technical-architecture-blueprint.md（Q1-Q10 + ADR + 9 节）
  - addendum-v1.0.md（6 条核心禁止 + MVP 边界 + 测试要求 + 目录规范 + Agent 5 阶段工作流）

## 6. 下一步建议（Phase 2 起步前需钉死的事）

| 编号 | 钉死事项 | 落点 | 优先级 |
|---|---|---|---|
| 钉死-1 | docs/ring-protocol.md 完整 schema（含 payload 字段、单位、坐标系、版本号） | docs/ring-protocol.md | 高 |
| 钉死-2 | 4 个 JSON Schema 独立成文件 | src/SharedSchema/schemas/*.json | 高 |
| 钉死-3 | 工具链 spike：JSON Schema → C# / TS 类型生成器选型 | src/SharedSchema/generate.ts + RingProtocol/build.ts | 高 |
| 钉死-4 | §6.2 时序预算与本骨架 P95 口径的优先级裁决 | docs/architecture/time-budget.md | 中 |
| 钉死-5 | §3.1 目录布局 vs 本骨架目录（src/ + tests/ + docs/ + packages/）的差异说明 | docs/architecture/repo-layout.md | 中 |
| 钉死-6 | WebView2 Runtime Bootstrapper 策略（自动装 vs 仅提示） | docs/architecture/webview2-bootstrap.md | 中（待澄清问题之一） |
| 钉死-7 | Dead Zone 取消策略（严格取消 vs 吸附到最近方向） | docs/architecture/dead-zone-policy.md | 中（待澄清问题之一） |
| 钉死-8 | 默认 Profile 数量上限（建议 5 个内置） | docs/architecture/default-profiles.md | 低（待澄清问题之一） |

---

> 本报告由 Flow Ring 主 Agent 在 Phase 1 阶段产出，作为后续 6 个阶段的输入基准。所有引用均指向 docs/architecture/ 下三份蓝图，自身不引入新约束。