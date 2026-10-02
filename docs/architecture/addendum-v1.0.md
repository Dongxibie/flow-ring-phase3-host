# Flow Ring v1.0 Addendum — 硬约束规范补充

> **本文件地位**：[v1.0-architecture.md](v1.0-architecture.md) 和 [technical-architecture-blueprint.md](technical-architecture-blueprint.md) 的**硬约束补充**。
> **强制力**：与两份蓝图同等强制。**有冲突时以本文件为准**。
> **触发时机**：Agent 启动开发 / Sprint 切换 / 蓝图修订前必读。
> **最后更新**：2026-10-02

---

## 补充 1：Development Non-Negotiable Rules（核心禁止事项）

6 条禁止，违反任何一条 → CI 必须 fail 或 code review 必须 reject。

### 规则 1.1：Ring 禁止直接调用 OS API

| 项目 | 内容 |
|---|---|
| **定义** | 任何 `RingNode` / `RingResolver` / `InputStateMachine` / `IDirectionResolver` 代码不得引用 `System.Windows.Forms` / Win32 P/Invoke / 任何 OS 特定命名空间 |
| **理由** | 破坏 RingCore 的 OS 无关性，未来跨平台（macOS/Linux）时这层代码无法迁移 |
| **检测** | CI 检查 `FlowRing.Core.csproj` 不引用任何 OS 特定包；编译时 Win32 命名空间引用 = build fail |
| **修复** | OS 操作走 `IDesktopBridge`；RingCore 只持有 `IDesktopBridge` 抽象引用 |

### 规则 1.2：Action 禁止保存 UI 信息

| 项目 | 内容 |
|---|---|
| **定义** | `ActionDef` / `IActionExecutor` / `IActionStep` / `ExecutionResult` 的字段不得包含 `Icon` / `Color` / `Position` / `Animation` / `DisplayHint` 等 UI 相关信息 |
| **理由** | Action 是执行器不是渲染器；UI 信息属于 Ring 层；耦合会导致"改 Action 改坏 UI"或反向 |
| **检测** | Action JSON Schema 不允许 UI 字段；xUnit 断言 `IActionExecutor.ExecuteAsync` 不接收 UI 类型参数 |
| **修复** | UI 信息由 Ring 层维护；Action 只返回 `ExecutionResult { success, error?, durationMs, artifacts }` |

### 规则 1.3：Context 禁止修改 Action

| 项目 | 内容 |
|---|---|
| **定义** | `IContextEngine` / `IContextDetector` / `Win32ContextEngine` 不得注册 Action、修改 ActionDef、调用 `IActionEngine.ExecuteAsync` |
| **理由** | Context 是只读事实陈述（"现在 VS Code 是前台"），不应有副作用；越权会导致 Context 与 Action 纠缠 |
| **检测** | `IContextEngine` 接口签名只能输出 `ApplicationContext`，不能调用 `IActionEngine` API；代码搜索 `context.*.RegisterAction` 必须无结果 |
| **修复** | Context 通过 `Profile Resolver` **间接**影响 Action 选择（Context → Profile → Action 列表），不直接干预 |

### 规则 1.4：UI 禁止直接访问 Storage

| 项目 | 内容 |
|---|---|
| **定义** | `FlowRing.UI` / `FlowRing.UI.Studio` 任何 `.tsx`/`.ts` 文件不得 `import fs/path/process` 等 Node.js API；不得直接 fetch 本地文件路径 |
| **理由** | 绕过 RingProtocol = 破坏跨进程边界，未来 WebView2 配置变更或迁移到独立打包时需要重写所有 IO |
| **检测** | ESLint 规则 `no-restricted-imports` 禁止 fs/path；CI 搜索 `import.*from\s+['"]fs['"]` 必须无结果 |
| **修复** | 所有 IO 走 DesktopHost 通过 `FlowRing.Protocol` 暴露；前端组件调 `useBridge().readProfile()` |

### 规则 1.5：Plugin 禁止绕过 Permission Layer

| 项目 | 内容 |
|---|---|
| **定义** | Plugin 不能直接调用 `System.IO.File` / `System.Net.Http` / Win32 API 等；只能通过 `IPluginHost` 暴露的包装 API |
| **理由** | 三层防御（声明式权限 + 用户授权 + 运行时校验）的核心就是强制所有 Plugin 调用走 Host API；绕过去 = 整个沙箱失效 |
| **检测** | Plugin 进程运行在受限 `PermissionSet`；运行时拦截反射调用；plugin.json 未声明的权限调用直接 throw `PermissionDeniedException` |
| **修复** | `IPluginHost` 是 Plugin 唯一对外接口；任何直接调用 OS API 的尝试在 `PluginLoadContext` 加载时被阻止 |

### 规则 1.6：Studio 禁止直接修改 Runtime 数据

| 项目 | 内容 |
|---|---|
| **定义** | `FlowRing.Studio.Core` 不得直接修改 `FlowRing.Core` 的运行时 IR（`RingNode` / `RingSlot` / `Profile`）；只能读写自己的 `StudioDocument` |
| **理由** | 运行时 IR ≠ 编辑时 Document；直接修改会污染 Runtime 状态、未保存改动、撤销历史、并发安全 |
| **检测** | Studio 项目依赖检查：`FlowRing.Studio.Core` 不得引用 `FlowRing.Core` 的 `internal` 类型，只能用 `IProfileStore` / `IProfileMetadata` 等公开接口 |
| **修复** | Studio 修改流程：`StudioDocument` → `Command.Apply()` → `CreateSnapshot()` → `ExportIR()` → `IProfileStore.SaveAsync()` |

### 检测手段汇总

| 规则 | CI 检测 | Code Review 检测 |
|---|---|---|
| 1.1 Ring 无 OS API | ✅ Roslyn Analyzer | ✅ |
| 1.2 Action 无 UI 字段 | ✅ JSON Schema 校验 | ✅ |
| 1.3 Context 不改 Action | ✅ 接口签名 + 调用图分析 | ✅ |
| 1.4 UI 无 fs/path | ✅ ESLint rule | ✅ |
| 1.5 Plugin 走 Host API | ✅ PermissionSet 沙箱 | ✅ |
| 1.6 Studio 不改 Runtime | ✅ 项目依赖检查 | ✅ |

---

## 补充 2：MVP Boundary（MVP 边界强制版）

### 必做（In Scope）— 6 步 MVP 主链路

```
① Mouse Hook            WH_MOUSE_LL 拦截侧键长按
        ↓
② Ring Display          8 向 + Dead Zone + Glass UI 渲染
        ↓
③ Direction Selection   30px 死区 + 8 向 Direction Lock
        ↓
④ Keyboard Action       SendInput + 多键序列 + 修饰键
        ↓
⑤ System Action         截图 / 音量调节 / 窗口最小化（起步三个）
        ↓
⑥ Profile Save          分文件 JSON + 原子写
```

### 不做（Out of Scope）— 反发散清单

| 类别 | 明确不做 | 引入版本 | 备注 |
|---|---|---|---|
| AI Action | ❌ | v1.1 | 接口 MVP 预留，不写实现 |
| Plugin 系统 | ❌ | v1.1 | 架构预留但不实现 |
| Cloud Sync | ❌ | v1.1 | MVP 只做本地 Flow Code 导入导出 |
| Marketplace | ❌ | v1.1 | — |
| Touch Pen | ❌ | v1.1 | — |
| macOS | ❌ | v2.0 | — |
| Workflow Action | ❌ | v1.1 | — |
| 自动更新（Squirrel.Update） | ❌ | v1.1 | MVP 用全量安装包升级 |
| 遥测 / Sentry | ❌ | v1.1 | — |
| 主题自定义 | ❌ | v1.1 | MVP 只一套 Glass 主题 |
| 动画自定义 | ❌ | v1.1 | MVP 固定动画时长 |
| Linux | ❌ | v2.0+ | — |
| 多语言 | ❌ | v1.1 | MVP 默认中文 + 英文 |
| Undo/Redo in MVP | ❌ | v1.0 Studio 含简化版 | 见补充 3 |

### 反发散条款

**Agent 在 Sprint 1-6 期间禁止主动实现上述任何 Out of Scope 功能**。即使架构上预留了接口（如 `IAIActionStep`），也不允许写完整实现。

发现 Agent 越界 → 立即暂停，输出"越界报告"，等待用户裁决。

---

## 补充 3：Testing Requirements（测试要求强制版）

### 测试矩阵

| 模块 | 测试类型 | 覆盖率要求 | Mock 策略 | 工具 |
|---|---|---|---|---|
| **FlowRing.Core** | Unit Test | **> 90%** | 无外部依赖，纯逻辑测试 | xUnit + FluentAssertions |
| **FlowRing.Action** | Executor Mock Test | **> 80%** | `IExecutor` mock 测 Engine；Engine mock 测 Executor | xUnit + NSubstitute |
| **FlowRing.Core.Context** | Rule Matching Test | **> 85%** | 真实 `ApplicationContext`，mock Win32 API | xUnit + NSubstitute |
| **FlowRing.Core.FlowCode** | Encrypt/Decrypt Round-trip | **100%** | 端到端 round-trip，无 mock | xUnit |
| **FlowRing.Studio.Core** | Document Undo/Redo Test | **> 85%** | rxjs observable + Command Stack 真实跑 | Vitest + Testing Library |
| **FlowRing.Protocol** | C# ↔ TS 一致性 Test | 100% | 序列化反序列化比对 | xUnit + Vitest |

### 测试实施规则

1. **TDD 优先**：先写测试 → 看到 fail → 写实现 → 看到 pass
2. **CI 必跑**：每次 PR 触发 `dotnet test` + `pnpm test` + `pnpm test:coverage`
3. **覆盖率门禁**：合并到 main 必须达到覆盖率要求，CI 失败 = 阻塞合并
4. **集成测试隔离**：Win32 集成测试用 `[Trait("Category", "Integration")]`，CI matrix 只跑 `windows-latest`
5. **性能测试**：每个 Sprint 必须有 BenchmarkDotNet 跑核心算法，输出到 `BenchmarkDotNet.Artifacts/`
6. **测试命名规范**：`MethodName_StateUnderTest_ExpectedBehavior`（如 `Resolve_DeadZoneDistance_ReturnsCenter`）

### MVP Sprint 测试交付物

| Sprint | 必交付测试 |
|---|---|
| Sprint 1（RingCore） | Direction 判定 + StateMachine 转换 + Ring 遍历 = > 50 个测试 |
| Sprint 2（Action） | Keyboard/System Executor + Pipeline Stage = > 30 个测试 |
| Sprint 3（Desktop Host） | Mouse Hook 集成测试 + Named Pipe 协议测试 = > 20 个测试 |
| Sprint 4（Ring UI） | RuntimeRing 渲染 + Direction 高亮组件测试 = > 25 个测试 |
| Sprint 5（Profile + Studio） | Profile CRUD + Document Undo/Redo + 三栏 Studio = > 40 个测试 |
| Sprint 6（Context + FlowCode） | Rule Matching + Encrypt/Decrypt Round-trip + 打包验证 = > 30 个测试 |

**总测试目标**：MVP 结束 > 195 个测试全绿，覆盖率 > 85%（综合）。

---

## 补充 4：Directory Conventions（强制目录规范）

### MVP 标准结构

```
flow-ring/
├── src/
│   ├── FlowRing.Core/                # RingCore 纯逻辑（OS 无关）
│   │   ├── Geometry/                 # Direction, Point, Angle
│   │   ├── StateMachine/             # InputStateMachine
│   │   ├── Ring/                     # RingNode, RingResolver
│   │   ├── Action/                   # IAactionStep 接口（执行实现在 FlowRing.Action）
│   │   ├── Context/                  # IContextEngine, ProfileResolver
│   │   ├── Profile/                  # Profile model
│   │   └── FlowCode/                 # FlowCode 编解码
│   ├── FlowRing.Action/              # Action Engine（独立顶级模块）
│   │   ├── Abstractions/             # IActionExecutor, IActionCapability, IActionPipelineStage
│   │   ├── Capabilities/
│   │   ├── Pipeline/                 # 内置 Stage
│   │   ├── Executors/                # KeyboardExecutor, SystemExecutor
│   │   └── Registry/                 # ActionExecutorRegistry
│   ├── FlowRing.DesktopBridge/       # OS 桥接层
│   │   ├── Abstractions/             # IDesktopBridge, IInputAdapter, IContextDetector
│   │   └── Win32/                    # Win32 实现
│   ├── FlowRing.Host/                # DesktopHost 主进程
│   │   ├── Program.cs
│   │   ├── Tray/
│   │   ├── Host/                     # WebView2Host, BridgeServer
│   │   └── Lifecycle/
│   ├── FlowRing.Protocol/            # C# ↔ TS 协议
│   │   ├── Messages.cs               # C# 协议消息
│   │   └── ts/                       # TypeScript 协议类型
│   └── FlowRing.UI/                  # React + TypeScript
│       ├── packages/
│       └── apps/
└── tests/
    ├── Core.Tests/                   # RingCore + Context + FlowCode 单测
    ├── Action.Tests/                 # Action Engine + Executor 单测
    └── Protocol.Tests/               # C# ↔ TS 协议一致性测试
```

### 命名规范

| 类型 | 命名规则 | 示例 |
|---|---|---|
| .NET 项目 | `FlowRing.{Module}` | `FlowRing.Core.csproj` |
| 测试项目 | `{Module}.Tests` | `Core.Tests.csproj` |
| .NET 命名空间 | `FlowRing.{Module}.{SubArea}` | `FlowRing.Core.Geometry` |
| TS 包 | `@flowring/{module}` | `@flowring/protocol` |
| C# 文件 | PascalCase | `DirectionResolver.cs` |
| TS 文件 | kebab-case | `ring-view.tsx` |
| 测试文件 | `{Subject}Tests.cs` / `{subject}.test.ts` | `DirectionResolverTests.cs` |

### 模块依赖规则（强制单向）

```
FlowRing.UI
  └─→ FlowRing.Protocol (TS types only)

FlowRing.Host
  ├─→ FlowRing.Core
  ├─→ FlowRing.Action
  ├─→ FlowRing.DesktopBridge
  └─→ FlowRing.Protocol (C# types)

FlowRing.Action
  └─→ FlowRing.Core

FlowRing.DesktopBridge
  └─→ FlowRing.Core

FlowRing.Core
  └─→ (nothing — 终极底层)
```

**禁止依赖**：
- ❌ `FlowRing.Core` → 任何其他模块
- ❌ `FlowRing.Core` → 任何 OS API
- ❌ `FlowRing.UI` → `FlowRing.Core`（只能通过 Protocol）
- ❌ `FlowRing.UI` → `FlowRing.Host`（只能通过 Protocol 通信）
- ❌ 任何循环依赖

### 目录结构与原蓝图的关系

原蓝图的目录结构（v1.0-architecture.md §3.1 / technical-architecture-blueprint.md §2.1）是**详细版本**，包含 Plugins/、Tools/、Installer/、Docs/ 等扩展目录。

**MVP 阶段以本文件 §4 的精简版本为准**。扩展目录在对应 Sprint 引入：
- `Plugins/` → Sprint 1.5（v1.1 启动时建）
- `Tools/` → Sprint 3 引入 `SchemaCodegen/`
- `Installer/` → Sprint 6 引入 `Squirrel/`

---

## 补充 5：Agent Working Mode（Agent 工作模式强制版）

### 5 阶段流程（每个 Sprint 强制遵循）

```
Phase 1: 理解架构
├── 读取 v1.0-architecture.md + technical-architecture-blueprint.md + 本 addendum
├── 输出："架构理解摘要"（1 段话，列出本 Sprint 涉及的模块和规则）
└── 不允许跳到 Phase 2

Phase 2: 创建/更新骨架
├── 确认 FlowRing.sln + 6 个 src/ 项目 + 3 个 tests/ 项目存在
├── 创建本 Sprint 需要的子目录（如 Sprint 1 → src/FlowRing.Core/Geometry/）
├── 不写实现代码
└── 输出："骨架完成报告"（项目列表 + 目录结构）

Phase 3: 实现（本 Sprint 范围）
├── 严格按 Sprint 边界实现
├── 不允许写 Sprint 2-6 的内容
├── 不允许写 Out of Scope 功能（补充 2）
├── 不允许违反补充 1 的 6 条禁止
└── 输出："实现完成报告"（文件列表 + 行数 + 关键设计点）

Phase 4: 测试
├── 编写单元测试（按补充 3 的覆盖率和数量要求）
├── 运行 dotnet test / pnpm test，全绿
├── 检查覆盖率达标
└── 输出："测试报告"（测试数 / 覆盖率 / 失败列表）

Phase 5: 汇报
├── 按汇报模板输出
│   ├── 完成度（100% / 部分完成 + 原因）
│   ├── 可演示产物（链接或路径）
│   ├── 测试结果（单元/集成/端到端测试数 + 覆盖率）
│   ├── 已知问题（列表）
│   ├── 下一 Sprint 重点（列表）
│   └── 性能基准（关键指标实测值 vs 目标值）
└── 等待用户确认才进 Sprint N+1
```

### 禁止事项

| 禁止 | 理由 | 检测方式 |
|---|---|---|
| 跨 Sprint 开发 | 一个 Sprint 只做自己的范围 | 用户验收时核对文件清单 |
| 一次性生成完整项目 | 违反 Phase 流程 | 用户验收时核对"骨架→实现→测试→汇报"四步是否齐全 |
| 跳过 Phase | 每个 Phase 必须输出报告 | 用户验收时核对 4 份报告 |
| Sprint 1 写 Sprint 2 的 Action Engine | 严格范围控制 | 用户验收时核对 Action 相关文件应不存在 |
| Phase 2 写 Phase 3 的实现代码 | 骨架阶段只能创建项目结构 | 用户验收时核对骨架报告无实现代码 |
| 在 Out of Scope 列表中主动添加功能 | 反发散条款 | 用户验收时核对新增功能是否在 Out of Scope 清单 |
| 违反 6 条核心禁止 | 补充 1 硬约束 | CI + Code Review |

### 触发条件

- 收到"启动 Sprint N"指令 → 进入 Phase 3-N（N=1 时 Phase 1+2 也走一遍）
- 收到"调整蓝图"指令 → 回到 Phase 1 重新理解，更新本 addendum
- 收到"输出 Sprint N 汇报"指令 → 直接进入 Phase 5
- 收到"跳过 Sprint"指令 → **拒绝执行**，要求确认（因为违反 MVP 边界）

### 汇报模板（每个 Sprint 结束必填）

```
## Sprint {N} 汇报

### Phase 1: 架构理解摘要
（1 段话，列出本 Sprint 涉及的模块 + 引用了哪些硬约束规则）

### Phase 2: 骨架完成报告
- 项目列表：...
- 新建目录：...
- 配置文件：...

### Phase 3: 实现完成报告
- 实现文件清单：...
- 关键设计点：...
- 边界检查（补充 1 6 条）：✅ / ❌ + 说明
- 反发散检查（补充 2 Out of Scope）：✅ / ❌ + 说明

### Phase 4: 测试报告
- 单元测试数：...
- 覆盖率：...%（要求 > 90%）
- dotnet test 结果：全绿 / 失败列表
- 性能基准：...

### Phase 5: 完成度与下一步
- 完成度：100% / 部分完成（原因）
- 可演示产物：路径
- 已知问题：...
- 下一 Sprint 重点：...

### 等待用户确认
- [ ] 用户确认 → 启动 Sprint N+1
- [ ] 用户要求调整 → 回到对应 Phase
```

---

## 附录：与两份蓝图的交叉引用

| 本 addendum 章节 | 对应蓝图章节 | 关系 |
|---|---|---|
| 补充 1（核心禁止 6 条） | 蓝图中没有独立章节 | **新增硬约束**，引用后纳入蓝图体系 |
| 补充 2（MVP 边界强制版） | v1.0 §8.1 / 深度版 §8.1 | **精炼版**，原章节保留为详细说明，本文件是强制版 |
| 补充 3（测试要求） | 蓝图中只有性能测试 | **新增功能测试要求**，补齐测试维度 |
| 补充 4（目录规范） | v1.0 §3.1 / 深度版 §2.1 | **MVP 标准版**（精简），原章节为详细版（含扩展目录） |
| 补充 5（Agent 工作模式） | v1.0 §7.2 / 深度版 §7.2 | **强化版**，强制 5 阶段流程 + 汇报模板 |

---

## 附录：规则冲突处理顺序

当两份蓝图与本 addendum 冲突时，按以下顺序处理：

1. **本 addendum**（最高优先，硬约束）
2. technical-architecture-blueprint.md（深度版，工程实施用）
3. v1.0-architecture.md（基线版，团队理解用）

**举例**：
- 蓝图说"Plugin 系统架构预留接口"，本 addendum 说"Plugin 系统不在 MVP" → **以本 addendum 为准**，架构可预留接口但 Sprint 1-6 不写实现
- 蓝图说"ProcessName 匹配"，本 addendum 没改动 → 以深度版 §3.4 为准

---

**本 addendum 是两份蓝图的硬约束补充，具有同等强制力。**
**最后更新**：2026-10-02
**维护者**：开发 Agent 必须在 Sprint 汇报中引用本文件相关章节
