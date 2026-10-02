# Flow Ring Technical Architecture Blueprint v1.0

> **本文件状态**：v1.0 Technical Architecture Blueprint（深度版）
> **前置基础**：[v1.0-architecture.md](v1.0-architecture.md)（已存在，作为基线）
> **本文新增**：融合 Q1-Q5 深度架构验证，整合 Action Engine 重设计、Flow Code 同步设计、Studio 独立模块、Plugin 架构
> **目标读者**：产品设计 / 前端工程师 / 桌面工程师 / 插件开发者

> **⚠️ 硬约束补充**：本蓝图受 [addendum-v1.0.md](addendum-v1.0.md) 强制约束，包含 6 条核心禁止 / MVP 边界 / 测试要求 / 目录规范 / Agent 工作模式。有冲突以 addendum 为准。MVP 阶段的具体目录规范和 Agent 工作模式以 addendum §4 和 §5 为准，本文件 §2 为详细扩展版。

---

# 第一部分：5 个深度架构问题回答

## Q1：Context Engine 为什么不能直接依赖 Ring Engine？

**职责的本质差异**：

- **Context Engine = 环境识别层**。它只回答一个问题："我现在在哪台机器、哪个应用、什么窗口、什么状态？"它的输出是 `ApplicationContext { processName, windowTitle, pid, ts }`——一份**事实陈述**。
- **Ring Engine = 动作编排层**。它只回答一个问题："根据当前可用的 Profile + Action 集合，我应该展示哪些 slot、按什么顺序、什么视觉规则？"它的输出是 `RingView { slots, layout, animations }`——一份**渲染指令**。

两者关注点正交：Context 不知道"动作"是什么，Ring 不知道"前台窗口"是什么。

**数据流方向（强制单向）**：

```
Context Source (Win32)
   ↓ ApplicationContext
Context Engine ─────────────┐
   ↓                         │ (Context Engine 永远不调用 Ring Engine)
Profile Resolver            │
   ↓ ActiveProfile           │
Ring Engine ←────────────────┘
   ↓ RingView
UI Renderer
```

关键：**Context Engine 不持有 Ring Engine 的引用**，**Ring Engine 不持有 Context Engine 的引用**。两者通过 **Profile Resolver** 这个翻译层连接，Profile Resolver 才是"知道两者都存在"的组件。

**避免耦合的具体做法**：

1. **接口分离**：`IContextEngine` 只输出 `IApplicationContext`，`IRingEngine` 只接受 `ActiveProfile` 作为输入。两者在类型层面就不允许互相引用。
2. **Context 缓存属于 Context Engine**：何时刷新缓存、TTL 多少、缓存键怎么设计，Ring Engine 一行不过问。
3. **Ring 渲染策略属于 Ring Engine**：动画时长、间距、视觉规则，Context Engine 完全不关心。
4. **测试隔离**：Context Engine 可单独用 fake ApplicationContext 测试；Ring Engine 可用 fake ActiveProfile 测试。两者永远不会要求对方存在。

**反例（如果耦合会怎样）**：Context Engine 在检测到 VS Code 时直接调用 Ring Engine 的 `SetActiveProfile("developer")`——这会让 Context Engine 失去"上下文无关的事实陈述"性质，未来想做"基于 Context 的统计"、"基于 Context 的日志"都做不了，因为它已经绑死在 Ring 上。

---

## Q2：重新设计 Action Engine，保证扩展时旧 Action 不修改

**核心思想：Capability-based + Strategy + Pipeline 三层架构**。

### 2.1 三层模型

```
┌─────────────────────────────────────────────────┐
│  Layer 1: Action Catalog (静态定义)              │
│  - ActionDef (持久化形态)                         │
│  - 不变，任何 Action 类型都映射到同一个 ActionDef │
└────────────────┬────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────────────┐
│  Layer 2: Action Capability (能力声明)           │
│  - IActionCapability { id, version, kind, scope}│
│  - Executor 注册时声明自己提供哪些 Capability    │
│  - 引擎按 Capability 匹配，不按 Kind 匹配        │
└────────────────┬────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────────────┐
│  Layer 3: Execution Pipeline (执行管道)          │
│  - Pre-Check → Context Inject → Execute →       │
│    Post-Process → Audit Log                      │
│  - 任何 Action 都走同一个 Pipeline               │
└─────────────────────────────────────────────────┘
```

### 2.2 核心接口（扩展点设计）

```csharp
// 不变：所有 Action 实现的统一接口
public interface IActionExecutor {
    ActionKind Kind { get; }                                          // Keyboard / System / Application / AI / Workflow
    IReadOnlyList<IActionCapability> Capabilities { get; }           // 声明能力
    ValueTask<ExecutionResult> ExecuteAsync(ActionDef def, ActionContext ctx, CancellationToken ct);
}

// 新增：能力声明（取代"按 Kind 硬编码选择 Executor"）
public sealed record IActionCapability(
    string Id,                  // "send-keystroke", "capture-screen", "call-llm"
    string Version,             // SemVer: "1.0.0"
    IReadOnlyList<string> Tags, // "input", "screenshot", "ai"
    PermissionTier Tier         // Safe / Normal / Dangerous
);

// 新增：Pipeline Stage（取代"每个 Executor 自己处理前后置逻辑"）
public interface IActionPipelineStage {
    string Name { get; }
    ValueTask<StageResult> BeforeAsync(ActionDef def, ActionContext ctx, CancellationToken ct);
    ValueTask<StageResult> AfterAsync(ActionDef def, ExecutionResult result, ActionContext ctx, CancellationToken ct);
}

// 新增：注册表（取代"硬编码 Executor 字典"）
public interface IActionExecutorRegistry {
    void Register(IActionExecutor executor);
    ValueTask<IActionExecutor?> ResolveAsync(ActionDef def, CancellationToken ct);
}
```

### 2.3 扩展示例：未来加入 AI Agent

**第 1 步**：写新 Executor（不动旧 Executor 一行）

```csharp
public sealed class OpenAIAgentExecutor : BaseActionExecutor {
    public override ActionKind Kind => ActionKind.AI;
    public override IReadOnlyList<IActionCapability> Capabilities => new[] {
        new IActionCapability("call-llm", "1.0.0", new[] { "ai", "nlp" }, PermissionTier.Dangerous)
    };
    protected override ValueTask<ExecutionResult> ExecuteCoreAsync(...) { /* ... */ }
}
```

**第 2 步**：注册到 Registry

```csharp
registry.Register(new OpenAIAgentExecutor());
registry.Register(new AnthropicAgentExecutor());
registry.Register(new LocalLLMExecutor());
```

**第 3 步**：完成。旧的 KeyboardExecutor、SystemExecutor **完全不需要改动**。

### 2.4 为什么这样能保证旧 Action 不修改？

1. **Executor 接口稳定**：SemVer 保证 major 版本内签名不变。
2. **注册而非硬编码**：新增 Executor = 注册，不是修改 Engine。
3. **Pipeline 通用**：所有 Action 都走同一条管道，不需要为新 Action 类型加 stage。
4. **Capability 匹配取代 Kind 匹配**：未来 "Workflow" 不再是特殊 Kind，而是一个能组合多个 Capability 的 Executor。
5. **测试兼容**：旧的 Executor 测试无需修改，Pipeline 测试一次覆盖所有 Action 类型。

---

## Q3：Flow Code 多设备同步的完整设计

### 3.1 数据结构：Snapshot + Patch + Vector Clock 三轨

```json
{
  "format": "FLOW-CODE-V2",
  "version": "2.0.0",
  "snapshotId": "snap-2026-10-02T12-00-00-a3f9",
  "vector": {
    "device-a": 42,
    "device-b": 17,
    "device-c": 8
  },
  "payload": {
    "profile": { /* Profile Schema */ },
    "actions": [ /* Action Schema */ ],
    "rules":   [ /* ContextRule[] */ ]
  },
  "delta": {
    "baseSnapshotId": "snap-2026-10-01T18-00-00-a3f9",
    "patches": [
      {
        "op": "replace",
        "path": "/ringGraph/nodes/dev-root/slots/Top/actionRef",
        "value": "git.commit"
      }
    ]
  },
  "checksum": "sha256-...",
  "encryption": {
    "algorithm": "AES-256-GCM",
    "kdf": "Argon2id",
    "salt": "...",
    "nonce": "..."
  }
}
```

**关键设计**：

- **Snapshot**：完整配置快照，用于冷启动和首次同步
- **Patch**：基于 JSON Patch (RFC 6902) 的增量，用于带宽优化
- **Vector Clock**：每台设备独立递增，用于检测并发编辑

### 3.2 Version 策略（三层版本号）

| 层级 | 用途 | 示例 |
|---|---|---|
| **Schema Version** | Profile/Action/Rule 的字段格式 | `profile.schemaVersion: "1.2"` |
| **Profile Version** | 业务语义版本（用户可见） | `profile.metadata.version: "2.1.0"` |
| **Device Version** | 单设备内的修改次数 | `vector.device-a: 42` |

**Schema Version 兼容规则**：

- v1 引擎读 v2 Profile：忽略新字段，保留旧字段
- v2 引擎读 v1 Profile：缺失字段用默认值填充
- v3+ v0 必须强制升级（不可降级）

### 3.3 Merge 策略：基于 (ringId, direction) 的自动 + 手动合并

**自动合并（无需用户介入）**：

- Metadata 字段（name、tags、description）→ 保留时间戳更新的
- ActionDef 字段（displayName、hotkey、tags）→ 同上
- PluginConfig 中的标量值 → 同上

**结构化冲突（需要用户选择）**：

- RingGraph 中**同一 (ringId, direction) 的 slot**被两台设备同时修改
- 解法：弹出 Conflict Resolver UI，列出 A/B/Base 三个版本让用户选

**冲突检测的 unique key**：

```
slot_unique_key = (ring_node_id, direction)
action_unique_key = (action_def_id)
rule_unique_key = (matcher_processName, matcher_windowTitle, priority)
```

这些 key 是天然的 CRDT-friendly key，配合 Vector Clock 可以无歧义合并。

**删除 vs 修改的冲突**：

- A 删了 Ring Node X，B 修改了 Ring Node X
- 解决：保留修改，提示用户"A 已删除此节点，但你修改了它，是否保留？"

### 3.4 安全策略

| 层面 | 策略 |
|---|---|
| **传输加密** | TLS 1.3（如果经过 Sync Server） |
| **存储加密** | AES-256-GCM，用户密码派生密钥 |
| **KDF** | Argon2id（time=3, memory=64MB, threads=4） |
| **设备配对** | Flow Code 头部包含 sender device ID + 6 位配对码 |
| **中继策略** | Sync Server 只存加密 blob，无法解密 |
| **用户控制** | 撤销设备授权、查看已配对设备列表 |
| **审计日志** | 所有同步事件本地记录 |
| **零知识证明** | 密码不离开本地，Server 永远见不到明文 |

**威胁模型**：

- Server 被攻陷：攻击者只拿到加密 blob，无密钥解不开
- 设备丢失：用户从其他设备撤销授权，旧设备的 Flow Code 自动失效
- 中间人攻击：TLS + Flow Code 内置 device signature

---

## Q4：Ring Studio 为什么必须独立，以及它的架构

### 4.1 为什么独立应用模块

**根本原因：Profile 的"运行时 IR"和"编辑时 Document"是两个不同的数据结构**。

- **运行时 IR (RingNode)**：紧凑、优化过、字段少、纯数据。
- **编辑时 Document (StudioDocument)**：富信息、含未保存的中间态、含撤销历史、含视图状态（哪些 panel 展开、选中哪个 slot）。

如果强行把 IR 当 Document 用，会出现：

1. 每次拖拽都触发 IO（不可接受）
2. 撤销栈需要侵入式补丁到 IR
3. UI 状态和业务状态耦合

所以 Studio 必须有自己的 Document Service 层，与运行时解耦。

### 4.2 Studio 架构

```
┌─────────────────────────────────────────────────────────────┐
│                    Ring Studio (独立模块)                   │
├─────────────────────────────────────────────────────────────┤
│  UI Layer (React + Glass)                                    │
│  ┌──────────┬───────────────────┬──────────────┐            │
│  │ Library  │   Canvas          │ Properties   │            │
│  │ Panel    │   (Live Preview)  │ Panel        │            │
│  └──────────┴───────────────────┴──────────────┘            │
├─────────────────────────────────────────────────────────────┤
│  View Layer (Zustand Store + React Subscription)            │
├─────────────────────────────────────────────────────────────┤
│  Document Service                                            │
│  ┌─────────────────────────────────────────────────┐        │
│  │  StudioDocument                                  │        │
│  │  ├─ profile (current Profile in editing)         │        │
│  │  ├─ selection (currently selected slot/node)     │        │
│  │  ├─ viewState (panel layouts, zoom, scroll)      │        │
│  │  └─ dirty (unsaved changes)                      │        │
│  └─────────────────────────────────────────────────┘        │
│  ┌─────────────────────────────────────────────────┐        │
│  │  Command Stack (Undo/Redo)                       │        │
│  │  ├─ ICommand { Do(), Undo(), Redo() }            │        │
│  │  ├─ AddSlotCommand                               │        │
│  │  ├─ RemoveSlotCommand                            │        │
│  │  ├─ BindActionCommand                            │        │
│  │  ├─ MoveToChildRingCommand                       │        │
│  │  └─ BatchCommand (compound)                      │        │
│  └─────────────────────────────────────────────────┘        │
│  ┌─────────────────────────────────────────────────┐        │
│  │  Change Tracker (rxjs-style observable)          │        │
│  │  └─ 订阅者：Canvas 预览 / Undo 状态 / 保存按钮  │        │
│  └─────────────────────────────────────────────────┘        │
├─────────────────────────────────────────────────────────────┤
│  Domain Services                                             │
│  - ProfileValidator (Schema 校验 + 业务规则)                 │
│  - ConflictResolver (合并 Flow Code 时)                     │
│  - TemplateEngine (模板应用)                                │
├─────────────────────────────────────────────────────────────┤
│  Persistence Layer                                           │
│  - DocumentStore (autosave to disk)                         │
│  - SnapshotManager (named snapshots)                        │
│  - HistoryStore (command history for replay)                │
└─────────────────────────────────────────────────────────────┘
```

### 4.3 五大子系统详解

#### (1) 编辑器（Editor）

**Document Service** 是核心：
- `StudioDocument`：当前编辑状态
- 所有 mutation 走 Command，不允许直接修改 Document
- Document 不可变快照 + Command 应用 = 当前状态（不可变数据结构的优势）

#### (2) 实时预览（Live Preview）

- Canvas 订阅 Document 的 `ChangeTracker`
- Document 变更 → Diff → Render
- 预览用**同一个 Ring UI 组件**（与 Runtime Ring 共享 `RingView` 组件），保证"Studio 里看到 = 运行时看到"
- 性能优化：Canvas 渲染走虚拟 DOM diff，不全量重绘

#### (3) Undo/Redo

- **Command Pattern**：每个操作是 `ICommand`，有 `Do/Undo/Redo`
- **双向链表**：`previous` 和 `next` 指针
- **批量操作**：`BatchCommand` 把多个 Command 打包（拖拽多个 slot 一次性撤销）
- **内存控制**：限制 stack 深度（如 100），溢出时压缩为 Snapshot
- **持久化**：Command history 可序列化到磁盘，下次打开 Studio 还能撤销

#### (4) History（历史）

- **Snapshot**：每次"应用"或"发布"创建一个命名快照（`2026-10-02 14:30 默认配置`）
- **Snapshot 存储**：走 Q6 已设计的 `IProfileStore.CreateSnapshotAsync`
- **History 视图**：左侧栏显示快照列表，可回退 / diff / 复制

#### (5) Template 系统

- **Template 定义**：`TemplateDef { name, description, ringGraph, suggestedActions, suggestedRules }`
- **Template 来源**：本地内置 / 用户自制 / 社区市场（v1.1）
- **Template 应用**：把 Template 的 ringGraph 合并到当前 Document，已存在的 slot 询问覆盖策略
- **Template Diff**：应用前显示 "将新增 X 个 slot，覆盖 Y 个 slot"

### 4.4 为什么它不是 Settings 页面

| 维度 | Settings 页面 | Ring Studio |
|---|---|---|
| **数据结构** | 表单字段 | 富 Document + IR 双向 |
| **修改语义** | 修改即生效 | Staging → 显式 Apply |
| **撤销** | 不需要 | 必须支持 |
| **预览** | 不需要 | 必须支持 |
| **路由** | `/settings/profile` | `/studio/profile/:id` |
| **打包** | 嵌入主程序 | 可独立打包 |
| **插件** | 无 | 可扩展 Editor 面板 |

**核心区别**：Settings 是"配置"，Studio 是"创作工具"。前者用户修改频率低，后者修改频率高、需要完整编辑体验。

---

## Q5：插件架构完整设计

### 5.1 插件能做什么

第三方开发者可创建：

- **新 Action 类型**：例如 `ObsidianActionExecutor`（操作 Obsidian 笔记）
- **新 Context Provider**：例如 `BrowserTabProvider`（识别当前 Chrome 标签页）
- **新 Executor**：例如 `SpotifyExecutor`（控制 Spotify）
- **新 Ring Visual**：例如 `CircularLayoutRenderer`
- **新 Studio 面板**：例如 `MetricsPanel`（显示 Action 使用频率）

### 5.2 插件生命周期

```
┌────────────────────────────────────────────────────────┐
│  Phase 1: Discovery                                     │
│  - Host 扫描 plugins/ 目录                             │
│  - 读取 plugin.json (manifest)                          │
│  - 校验 manifest 完整性                                 │
│  - 检查 Plugin SDK 版本兼容性                           │
└────────────────┬───────────────────────────────────────┘
                 ↓
┌────────────────────────────────────────────────────────┐
│  Phase 2: Load                                          │
│  - .NET Assembly: AssemblyLoadContext 加载              │
│  - WASM: Wasmer/Wasmtime 加载（v1.1）                   │
│  - 独立进程: Process.Start（最高安全级别）              │
│  - 失败：记录日志，跳过此插件                           │
└────────────────┬───────────────────────────────────────┘
                 ↓
┌────────────────────────────────────────────────────────┐
│  Phase 3: Initialize                                    │
│  - 创建 PluginContext（隔离的 API 客户端）              │
│  - 调用 IPlugin.OnInitialize(PluginContext)             │
│  - 失败：Unload + 通知用户                              │
└────────────────┬───────────────────────────────────────┘
                 ↓
┌────────────────────────────────────────────────────────┐
│  Phase 4: Register                                      │
│  - 插件调用 Registry.RegisterAction / RegisterContext   │
│  - 插件调用 Registry.RegisterPanel / RegisterRenderer   │
│  - Host 校验权限（plugin.json 声明的能力）              │
│  - 越权：拒绝注册 + 报告给用户                          │
└────────────────┬───────────────────────────────────────┘
                 ↓
┌────────────────────────────────────────────────────────┐
│  Phase 5: Activate                                      │
│  - 订阅事件: Subscribe(ContextChanged, RingOpened)      │
│  - Host 按订阅分发事件                                  │
└────────────────┬───────────────────────────────────────┘
                 ↓
┌────────────────────────────────────────────────────────┐
│  Phase 6: Runtime                                       │
│  - 执行 Action（受权限约束）                            │
│  - 提供 Context（受频率限制）                           │
│  - 提供 UI Panel（独立 React root）                     │
└────────────────┬───────────────────────────────────────┘
                 ↓
┌────────────────────────────────────────────────────────┐
│  Phase 7: Deactivate                                    │
│  - 用户/Host 触发停用                                   │
│  - 调用 IPlugin.OnDeactivate()                          │
│  - 取消订阅，清理资源                                   │
└────────────────┬───────────────────────────────────────┘
                 ↓
┌────────────────────────────────────────────────────────┐
│  Phase 8: Unload                                        │
│  - AssemblyLoadContext.Unload()                         │
│  - 等待 GC（最多 5s）                                   │
│  - 失败：记录但允许主进程继续                           │
└────────────────────────────────────────────────────────┘
```

### 5.3 API 接口设计（稳定层）

```csharp
// Host → Plugin 提供的 API
public interface IPluginContext {
    IPluginHost Host { get; }
    IPluginLogger Logger { get; }
    IPluginStorage Storage { get; }       // 隔离的存储空间
    IPluginEventBus Events { get; }
    CancellationToken AppStopping { get; }
}

public interface IPluginHost {
    // 注册 Action
    void RegisterAction(ActionDef def, IActionExecutor executor);

    // 注册 Context Provider
    void RegisterContextProvider(IContextProvider provider);

    // 注册 Studio Panel
    void RegisterStudioPanel(PanelDescriptor panel);

    // 订阅/取消订阅事件
    Guid Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IPluginEvent;
    void Unsubscribe(Guid subscriptionId);

    // 获取系统信息（只读）
    ISystemInfo SystemInfo { get; }
}

public interface IPluginStorage {
    // 每个插件独立的 Key-Value 存储
    ValueTask<string?> GetAsync(string key, CancellationToken ct);
    ValueTask SetAsync(string key, string value, CancellationToken ct);
    ValueTask DeleteAsync(string key, CancellationToken ct);
}

// Plugin → Host 实现的接口（插件开发者实现）
public interface IPlugin {
    string Id { get; }                    // 反向域名风格
    string Name { get; }
    Version Version { get; }
    Version RequiredHostApiVersion { get; }

    ValueTask OnInitializeAsync(IPluginContext context, CancellationToken ct);
    ValueTask OnDeactivateAsync(CancellationToken ct);
    ValueTask OnUnloadAsync(CancellationToken ct);
}

// Plugin manifest (plugin.json)
{
  "id": "com.example.obsidian-flow",
  "name": "Obsidian Flow",
  "version": "1.2.0",
  "minHostVersion": "1.0.0",
  "maxHostVersion": "2.0.0",
  "permissions": [
    "context:provide",
    "action:execute:safe",
    "storage:plugin-local"
  ],
  "entryPoint": "bin/ObsidianFlow.dll",
  "author": {...},
  "homepage": "https://..."
}
```

### 5.4 权限控制（三层防御）

**第一层：声明式权限（plugin.json）**

插件必须声明所有用到的权限，未声明的 API 调用会被拒绝。

```json
"permissions": [
  "context:provide:browser-tab",   // 提供 Context Provider
  "action:execute:safe",            // 执行 Safe 级 Action
  "storage:plugin-local",           // 使用本地存储
  "event:subscribe:context-changed" // 订阅 Context 变更事件
]
```

**第二层：用户授权（首次安装弹窗）**

首次安装插件时，弹出权限说明，用户显式同意：

```
┌─────────────────────────────────────────┐
│  安装插件: Obsidian Flow                 │
│                                         │
│  该插件请求以下权限：                    │
│  • 提供前台应用上下文（Chrome 标签页）   │
│  • 执行安全级动作（写入 Obsidian 笔记）  │
│  • 使用本地存储（最多 10MB）            │
│  • 订阅 Context 变更事件                │
│                                         │
│  [取消]                    [授权安装]   │
└─────────────────────────────────────────┘
```

**第三层：运行时校验（Host 强制）**

Host 在每次插件调用 API 时校验：
1. 是否有声明权限
2. 是否在用户授权范围内
3. 调用频率是否超限（如 Context Provider 最多 1Hz）
4. 资源使用是否超限（存储空间、内存）

### 5.5 版本兼容策略

**Plugin SDK SemVer**：

- `HostApi.Version`：当前 Host 提供的 Plugin SDK 版本
- `Plugin.RequiredHostApiVersion`：插件需要的最低版本
- `Plugin.MaxHostApiVersion`：插件能支持的最高版本（可选）

**兼容窗口**：

- 旧插件（HostApi 1.x）能在 Host 1.x、2.x 上跑
- 新插件（HostApi 2.x）不能在 Host 1.x 上跑
- Host 升级时，旧插件自动进入"兼容模式"（警告用户）

**Compatibility Matrix**：

```
┌────────────┬──────────┬──────────┬──────────┐
│            │ Plugin 1 │ Plugin 2 │ Plugin 3 │
├────────────┼──────────┼──────────┼──────────┤
│ Host 1.x   │    ✓     │    ✗     │    ✗     │
│ Host 2.x   │    ✓     │    ✓     │    ✗     │
│ Host 3.x   │  ⚠ deprec│    ✓     │    ✓     │
└────────────┴──────────┴──────────┴──────────┘
```

### 5.6 沙箱机制

**进程内沙箱（默认）**：

- AssemblyLoadContext 隔离 Assembly
- 受限 API Surface（仅暴露 IPluginHost 接口）
- 反射禁止（`PermissionSet` 限制）
- 网络访问默认禁止，需声明

**进程外沙箱（高级插件可选）**：

- 插件作为独立进程运行
- 通过 Named Pipe / Unix Socket 与 Host 通信
- 性能略差但安全性极高
- 适合需要本地 HTTP 服务器的插件

---

# 第二部分：Flow Ring Technical Architecture Blueprint v1.0

---

## 1. System Architecture Overview

### 1.1 七层分层模型

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer 7: Plugin Ecosystem (v1.1)                               │
│           第三方插件、模板市场                                  │
├─────────────────────────────────────────────────────────────────┤
│  Layer 6: Application Layer                                     │
│           RingStudio / ProfileManager / FlowCode Center         │
├─────────────────────────────────────────────────────────────────┤
│  Layer 5: Domain Services                                       │
│           Profile Resolver / Context Cache / FlowCode Sync      │
├─────────────────────────────────────────────────────────────────┤
│  Layer 4: Ring Engine                                            │
│           Direction Resolver / State Machine / Ring Resolver    │
├─────────────────────────────────────────────────────────────────┤
│  Layer 3: Action Engine (Capability-based)                       │
│           Executor Registry / Pipeline / Capability Resolver    │
├─────────────────────────────────────────────────────────────────┤
│  Layer 2: Context Engine                                         │
│           Application Detector / Context Cache / Rule Matcher   │
├─────────────────────────────────────────────────────────────────┤
│  Layer 1: Input Layer + OS Bridge                                │
│           InputAdapter (Mouse/Keyboard/Pen) / Win32 Hook / IPC  │
└─────────────────────────────────────────────────────────────────┘
            ↓                          ↑
        User Action              OS Notification
            ↓                          ↑
        Application / System / Hardware
```

### 1.2 每层职责、输入输出、为什么独立

#### Layer 1: Input Layer + OS Bridge

| 项目 | 内容 |
|---|---|
| **职责** | 捕获原始输入事件，转换为统一的 SpatialIntentEvent；提供 OS API 桥接 |
| **输入** | 鼠标 / 键盘 / 触控笔的硬件事件 |
| **输出** | SpatialIntentEvent 流 + RawInputEvent 流（用于 UI mousemove） |
| **为什么独立** | 三种输入设备的语义维度不同，独立后才能扩展触控笔等新设备 |
| **通信** | 向 Layer 4 推 SpatialIntentEvent；调用 OS API 通过 P/Invoke |

#### Layer 2: Context Engine

| 项目 | 内容 |
|---|---|
| **职责** | 识别当前前台应用 + 窗口状态 + 用户上下文 |
| **输入** | OS 通知（前台窗口变化） + 主动查询请求 |
| **输出** | ApplicationContext { processName, windowTitle, pid, ts } |
| **为什么独立** | Context Engine 不知道 Ring 的存在；Context 可被多处消费（日志、统计、未来 AI） |
| **通信** | 提供 `IContextDetector.DetectAsync()` API；提供 `ContextChanged` 事件 |

#### Layer 3: Action Engine（Capability-based）

| 项目 | 内容 |
|---|---|
| **职责** | 注册 Executor、按 Capability 匹配、执行 Pipeline（Pre-Check → Execute → Post-Process） |
| **输入** | ActionDef（从 Ring 解析得到） + ActionContext（含当前 Context） |
| **输出** | ExecutionResult { success, error?, durationMs, artifacts } |
| **为什么独立** | Action 是产品核心；Executor 可扩展（v1.1 加 AI、Plugin 加新 Executor）；不依赖 Ring 的渲染 |
| **通信** | Layer 4 调用 `IExecutorRegistry.ResolveAsync(def)`；Executor 通过 OS Bridge 执行 |

#### Layer 4: Ring Engine

| 项目 | 内容 |
|---|---|
| **职责** | 状态机管理（Idle/Pressed/.../Closing）；方向判定；Ring 图解析；选区高亮 |
| **输入** | SpatialIntentEvent（Layer 1）+ ActiveProfile（Layer 5 via Profile Resolver） |
| **输出** | RingView（渲染指令） + 选中状态变更事件 |
| **为什么独立** | Ring 是布局逻辑，与 Context / Action 完全解耦；可独立测试；可独立扩展渲染策略 |
| **通信** | 订阅 Layer 1 的事件；调用 Layer 5 的 Profile Resolver；向 Layer 6 推 RingView |

#### Layer 5: Domain Services

| 项目 | 内容 |
|---|---|
| **职责** | Profile Resolver（Context + Rules → ActiveProfile）；FlowCode Codec（编解码）；Context Cache |
| **输入** | Context 变更事件 + 配置文件变更 |
| **输出** | ActiveProfile + FlowCode 包 |
| **为什么独立** | Domain 是"业务规则"，不依赖具体 UI 或 OS；可被 Layer 4 和 Layer 6 共享消费 |
| **通信** | 提供 Profile 加载/保存 API；订阅 Context 变更 |

#### Layer 6: Application Layer

| 项目 | 内容 |
|---|---|
| **职责** | Studio 编辑器、Profile Manager UI、FlowCode Center UI、Settings UI |
| **输入** | 用户操作 + Domain Services 的 API |
| **输出** | Profile 变更请求 + UI 渲染 |
| **为什么独立** | UI 层不直接调用 Layer 1/2/3，避免越层；UI 团队可独立工作 |
| **通信** | 通过 RingProtocol（WebMessage + Named Pipe）调用 Layer 5/4/3 |

#### Layer 7: Plugin Ecosystem (v1.1)

| 项目 | 内容 |
|---|---|
| **职责** | 第三方 Action / Context Provider / Studio Panel |
| **输入** | Host 提供的 IPluginContext API |
| **输出** | 插件注册的 Action / Context / UI |
| **为什么独立** | 插件代码不可信，必须沙箱化；独立 SDK 版本管理 |
| **通信** | 进程内 AssemblyLoadContext / 进程外 Named Pipe |

### 1.3 层间通信协议

| 上游 → 下游 | 协议 | 频率 | 时延要求 |
|---|---|---|---|
| Layer 1 → Layer 4 | C# Event | 60-1000 Hz | < 1ms |
| Layer 2 → Layer 5 | C# Event + Cache | < 1 Hz | < 5ms |
| Layer 5 → Layer 4 | C# Method Call | per-trigger | < 5ms |
| Layer 4 → Layer 3 | C# Method Call | per-action | < 30ms |
| Layer 3 → Layer 1 (OS) | P/Invoke | per-action | < 30ms |
| Layer 6 → Layer 4/5/3 | RingProtocol (Named Pipe) | per-ui-op | < 50ms |
| Layer 7 → Layer 3/2 | IPluginHost API | per-call | < 30ms |

---

## 2. Repository Architecture

### 2.1 目录布局

```
FlowRing/
├── Core/                            # RingCore + DesktopBridge + DesktopHost
│   ├── FlowRing.RingCore/           # 纯逻辑，OS 无关
│   │   ├── Geometry/
│   │   ├── StateMachine/
│   │   ├── Ring/
│   │   ├── Action/
│   │   │   ├── Abstractions/
│   │   │   ├── Capabilities/
│   │   │   ├── Pipeline/
│   │   │   ├── Executors/          # 内置 Executor
│   │   │   └── Registry/
│   │   ├── Context/
│   │   ├── Profile/
│   │   ├── FlowCode/
│   │   └── Studio/                  # Document Service + Command Stack
│   ├── FlowRing.DesktopBridge/      # OS 桥接
│   │   ├── Abstractions/
│   │   ├── Win32/
│   │   └── Native/
│   ├── FlowRing.DesktopHost/        # 主进程
│   │   ├── Program.cs
│   │   ├── Tray/
│   │   ├── Host/
│   │   ├── Update/
│   │   └── Crash/
│   └── FlowRing.PluginHost/         # 插件宿主 (v1.1)
│
├── RingUI/                          # 运行时 UI
│   ├── packages/
│   │   ├── flowring-protocol/      # 共享协议类型
│   │   ├── flowring-bridge/        # WebView2 ↔ C# 通信
│   │   ├── flowring-glass-ui/      # Glass 组件库
│   │   └── flowring-state/         # Zustand stores
│   ├── apps/
│   │   ├── runtime/                # RuntimeRing 页面
│   │   └── shell/                  # 应用外壳 + 路由
│   └── tests/
│
├── Studio/                          # Ring Studio (独立模块)
│   ├── FlowRing.Studio.Core/        # Document Service + Command Stack
│   ├── FlowRing.Studio.UI/          # Studio 的 React UI
│   ├── FlowRing.Studio.Templates/   # 模板引擎
│   └── tests/
│
├── Plugins/                         # 插件 SDK (v1.1)
│   ├── FlowRing.Plugin.Sdk/         # IPlugin / IPluginHost 接口
│   ├── FlowRing.Plugin.Templates/   # 插件项目模板
│   └── samples/
│       ├── Sample.ActionPlugin/
│       ├── Sample.ContextPlugin/
│       └── Sample.StudioPanel/
│
├── Storage/                         # 持久化层
│   ├── FlowRing.Storage.Json/       # JSON 实现
│   ├── FlowRing.Storage.Snapshots/  # Snapshot 管理
│   └── FlowRing.Storage.Logs/       # ExecutionLog / CrashDump
│
├── Tests/
│   ├── FlowRing.UnitTests/          # RingCore / Domain
│   ├── FlowRing.IntegrationTests/   # DesktopBridge / 真实 Win32
│   ├── FlowRing.UiTests/            # Playwright + WebView2
│   └── FlowRing.PerfTests/          # 性能基准
│
├── Tools/
│   ├── SchemaCodegen/               # JSON Schema → C#/TS
│   ├── BenchmarkHarness/            # 性能基准
│   └── Packager/                    # Squirrel
│
├── Installer/
│   ├── squirrel/
│   └── wix/
│
├── Docs/
│   ├── architecture/
│   │   ├── v1.0-architecture.md
│   │   └── technical-architecture-blueprint.md  # 本文档
│   ├── specs/
│   ├── adr/
│   └── runbooks/
│
├── Scripts/
├── .editorconfig
├── .gitignore
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── pnpm-workspace.yaml
├── FlowRing.sln
└── README.md
```

### 2.2 技术选择

| 模块 | 技术 | 理由 |
|---|---|---|
| Core / RingCore | C# / .NET 8 | 跨平台 + Win32 P/Invoke 完善 |
| Core / DesktopHost | C# / WinForms (托盘) | WinForms 托盘最稳定 |
| RingUI | TypeScript / React 18 / Vite | 跨平台 + Studio 编辑器生态 |
| RingUI / 样式 | CSS Variables + Glass Design Tokens | 主题化 + 暗色模式 |
| RingUI / 状态 | Zustand | 轻量 + TS 友好 |
| Studio | TypeScript / React + rxjs | Change tracking 友好 |
| Storage | System.Text.Json + 原子写 | .NET 原生 + 无依赖 |
| Tests (C#) | xUnit + FluentAssertions + NSubstitute | 行业标准 |
| Tests (TS) | Vitest + Testing Library + Playwright | 现代 + 快 |
| Perf | BenchmarkDotNet | .NET 性能测试标准 |

### 2.3 依赖关系（强制单向）

```
RingUI ──→ RingProtocol ──→ (无)
  ↑                            ↑
  │                            │
  │ Named Pipe / WebMessage    │
  │                            │
Studio ──→ RingProtocol        │
  ↑                            │
  │                            │
  └────────────────────────────┤
                               │
Core ──→ Plugin.Sdk (v1.1) ────┘
  │
  └──→ DesktopBridge ──→ RingCore
```

**禁止的依赖**：

- RingCore ❌→ 任何 UI
- RingCore ❌→ 任何 OS API
- DesktopBridge ❌→ RingUI / Studio
- RingUI ❌→ RingCore（只能通过 Protocol）
- Studio ❌→ RingUI Runtime（共享 Components，但不走引用）

---

## 3. Core Module Design

### 3.1 RingCore

#### 3.1.1 数据模型

```csharp
// 8 向 + Dead Zone
public enum Direction {
    Top, TopRight, Right, BottomRight,
    Bottom, BottomLeft, Left, TopLeft,
    Center
}

// Ring 节点（图节点，不是树节点）
public sealed class RingNode {
    public required string Id { get; init; }
    public required string ProfileId { get; init; }
    public string? ParentRingId { get; init; }
    public int Depth { get; init; }                  // 1, 2, or 3
    public required string DisplayName { get; init; }
    public IReadOnlyDictionary<Direction, RingSlot> Slots { get; init; } = new Dictionary<Direction, RingSlot>();
}

// Slot 是 sealed hierarchy
public abstract class RingSlot {
    public required Direction Direction { get; init; }
}
public sealed class EmptySlot : RingSlot { }
public sealed class ActionSlot : RingSlot {
    public required string ActionRef { get; init; }     // → ActionDef.Id
}
public sealed class ChildRingSlot : RingSlot {
    public required string ChildRingId { get; init; }   // → RingNode.Id
}
public sealed class GroupSlot : RingSlot {               // v1.1，组合多个 action
    public required IReadOnlyList<string> ActionRefs { get; init; }
}

// Profile = Ring Graph + 引用 + Rules + Config
public sealed class Profile {
    public required ProfileMetadata Metadata { get; init; }
    public required RingGraph RingGraph { get; init; }
    public required IReadOnlyList<string> ActionRefs { get; init; }
    public required IReadOnlyList<ContextRule> ContextRules { get; init; }
    public PluginConfig? PluginConfig { get; init; }
    public AISettings? AISettings { get; init; }       // v1.1
}

public sealed class RingGraph {
    public required string RootId { get; init; }
    public required IReadOnlyDictionary<string, RingNode> Nodes { get; init; }
}
```

#### 3.1.2 Selection Algorithm

```csharp
public interface IDirectionResolver {
    /// 核心方向判定：当前鼠标点 → 8 向 / Dead Zone
    Direction Resolve(
        PointF center,
        PointF current,
        float deadZoneRadiusPx = 30f
    );
}

public sealed class GeometricDirectionResolver : IDirectionResolver {
    public Direction Resolve(PointF center, PointF current, float deadZoneRadiusPx = 30f) {
        var dx = current.X - center.X;
        var dy = current.Y - center.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);

        if (distance < deadZoneRadiusPx) return Direction.Center;

        // 计算角度（0° = East, 90° = South in screen coords）
        var angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        if (angle < 0) angle += 360f;

        // 8 向每向 45°，边界在 22.5°、67.5° 等
        // 屏幕 Y 轴向下，所以 Top = -90° = 270°
        return angle switch {
            >= 337.5f or < 22.5f  => Direction.Right,
            >= 22.5f  and < 67.5f => Direction.BottomRight,
            >= 67.5f  and < 112.5f => Direction.Bottom,
            >= 112.5f and < 157.5f => Direction.BottomLeft,
            >= 157.5f and < 202.5f => Direction.Left,
            >= 202.5f and < 247.5f => Direction.TopLeft,
            >= 247.5f and < 292.5f => Direction.Top,
            >= 292.5f and < 337.5f => Direction.TopRight,
            _ => Direction.Center
        };
    }
}
```

#### 3.1.3 RingResolver

```csharp
public interface IRingResolver {
    /// 根据当前 Ring + Direction，返回下一个状态
    RingResolutionResult Resolve(RingNode currentRing, Direction direction);
}

public abstract record RingResolutionResult;
public sealed record ActionTriggered(ActionDef Action) : RingResolutionResult;
public sealed record EnterChildRing(RingNode ChildRing) : RingResolutionResult;
public sealed record Cancelled : RingResolutionResult;
public sealed record InvalidDirection : RingResolutionResult;
```

### 3.2 Input Layer

#### 3.2.1 InputAdapter

```csharp
public interface IInputAdapter : IAsyncDisposable {
    /// 标准化事件（用于状态机）
    event EventHandler<SpatialIntentEvent> IntentEmitted;
    /// 原始事件（用于 UI 实时渲染）
    event EventHandler<RawInputEvent> RawInputEmitted;

    Task InstallAsync(CancellationToken ct);
    Task UninstallAsync(CancellationToken ct);
}

public readonly record struct SpatialIntentEvent(
    TriggerType TriggerType,        // Mouse / Keyboard / Pen
    PointF? OriginPoint,             // null for keyboard
    float Pressure,
    long TimestampMs,
    ModifierState Modifiers
);

public readonly record struct RawInputEvent(
    RawInputKind Kind,               // MouseMove / MouseDown / MouseUp / KeyDown / KeyUp
    PointF Position,
    int Button,
    ModifierState Modifiers,
    long TimestampMs
);

// Win32 实现（节选）
public sealed class MouseHookAdapter : IInputAdapter {
    private IntPtr _hookId;
    private HookProc? _hookProc;
    private POINT _pressOrigin;

    public event EventHandler<SpatialIntentEvent>? IntentEmitted;
    public event EventHandler<RawInputEvent>? RawInputEmitted;

    public Task InstallAsync(CancellationToken ct) {
        _hookProc = HookCallback;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, ...);
        return Task.CompletedTask;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var msg = (MouseMessage)wParam;

            // 推送 RawInputEvent 给 UI
            RawInputEmitted?.Invoke(this, new RawInputEvent(
                msg switch {
                    WM_MOUSEMOVE => RawInputKind.MouseMove,
                    WM_LBUTTONDOWN => RawInputKind.MouseDown,
                    WM_LBUTTONUP => RawInputKind.MouseUp,
                    _ => RawInputKind.Unknown
                },
                new PointF(mouse.pt.x, mouse.pt.y),
                ...
            ));

            // 触发时（按下）记录 origin，按住时持续发 SpatialIntentEvent
            if (msg == WM_LBUTTONDOWN || msg == WM_XBUTTONDOWN) {
                _pressOrigin = mouse.pt;
                IntentEmitted?.Invoke(this, new SpatialIntentEvent(...));
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
```

#### 3.2.2 State Machine

```csharp
public enum InputState {
    Idle,
    Pressed,
    HoldDetected,
    RingOpening,
    Selecting,
    Executing,
    Closing
}

public interface IInputStateMachine {
    InputState CurrentState { get; }
    event EventHandler<InputState> StateChanged;

    void ProcessEvent(SpatialIntentEvent ev, IStateMachineContext ctx);
}

public sealed class InputStateMachine : IInputStateMachine {
    private InputState _state = InputState.Idle;
    private DateTime _pressedAt;

    public void ProcessEvent(SpatialIntentEvent ev, IStateMachineContext ctx) {
        switch (_state, ev.TriggerType) {
            case (InputState.Idle, TriggerType.Mouse) when ev.Modifiers.HasFlag(ModifierState.SideButton):
                _state = InputState.Pressed;
                _pressedAt = DateTime.UtcNow;
                break;

            case (InputState.Pressed, _) when (DateTime.UtcNow - _pressedAt).TotalMilliseconds >= ctx.HoldThresholdMs:
                _state = InputState.HoldDetected;
                RaiseStateChanged();
                ctx.RequestOpenRing();
                _state = InputState.RingOpening;
                break;

            case (InputState.Selecting, TriggerType.Mouse) when /* mouse up */:
                _state = InputState.Executing;
                RaiseStateChanged();
                var result = ctx.ResolveSelectedAction();
                if (result is ActionTriggered at) ctx.ExecuteAction(at.Action);
                _state = InputState.Closing;
                ctx.RequestCloseRing();
                _state = InputState.Idle;
                break;

            // ... 其他转换
        }
    }
}
```

### 3.3 Action Engine（Capability-based 重设计版）

```csharp
// === Layer 1: 静态定义（不变） ===
public enum ActionKind { Keyboard, System, Application, AI, Workflow }

public sealed record ActionDef(
    string Id,
    ActionKind Kind,
    string DisplayName,
    string? IconHint,
    string? Hotkey,
    string Version,
    IReadOnlyList<string> Tags,
    ActionPayload Payload
);

public abstract record ActionPayload;
public sealed record KeyboardPayload(string Keys, KeyModifiers Modifiers) : ActionPayload;
public sealed record SystemPayload(SystemOp Op, JsonElement Args) : ActionPayload;
public sealed record ApplicationPayload(string ProcessName, string Command) : ActionPayload;
public sealed record AIPayload(string PromptTemplate, ModelRef Model) : ActionPayload;
public sealed record WorkflowPayload(IReadOnlyList<string> StepActionRefs) : ActionPayload;

// === Layer 2: 能力声明（取代"按 Kind 硬编码"） ===
public sealed record IActionCapability(
    string Id,
    string Version,
    IReadOnlyList<string> Tags,
    PermissionTier Tier
);

public enum PermissionTier { Safe, Normal, Dangerous }

// === Layer 3: Pipeline（统一执行链） ===
public interface IActionPipelineStage {
    string Name { get; }
    ValueTask<StageResult> BeforeAsync(ActionDef def, ActionContext ctx, CancellationToken ct);
    ValueTask<StageResult> AfterAsync(ActionDef def, ExecutionResult result, ActionContext ctx, CancellationToken ct);
}

// 内置 Stage
public sealed class PermissionCheckStage : IActionPipelineStage { ... }
public sealed class ContextInjectStage : IActionPipelineStage { ... }
public sealed class AuditLogStage : IActionPipelineStage { ... }
public sealed class MetricsStage : IActionPipelineStage { ... }

// === Executor 接口 ===
public interface IActionExecutor {
    ActionKind Kind { get; }
    IReadOnlyList<IActionCapability> Capabilities { get; }
    ValueTask<ExecutionResult> ExecuteAsync(ActionDef def, ActionContext ctx, CancellationToken ct);
}

// 抽象基类（简化新 Executor 开发）
public abstract class BaseActionExecutor : IActionExecutor {
    public abstract ActionKind Kind { get; }
    public abstract IReadOnlyList<IActionCapability> Capabilities { get; }
    protected abstract ValueTask<ExecutionResult> ExecuteCoreAsync(ActionDef def, ActionContext ctx, CancellationToken ct);

    public async ValueTask<ExecutionResult> ExecuteAsync(ActionDef def, ActionContext ctx, CancellationToken ct) {
        var sw = Stopwatch.StartNew();
        try {
            return await ExecuteCoreAsync(def, ctx, ct);
        } catch (Exception ex) {
            return new ExecutionResult(false, ex.Message, sw.ElapsedMilliseconds, null);
        }
    }
}

// === 注册表 ===
public sealed class ActionExecutorRegistry : IActionExecutorRegistry {
    private readonly List<IActionExecutor> _executors = new();

    public void Register(IActionExecutor executor) {
        _executors.Add(executor);
    }

    public ValueTask<IActionExecutor?> ResolveAsync(ActionDef def, CancellationToken ct) {
        var match = _executors.FirstOrDefault(e =>
            e.Kind == def.Kind &&
            e.Capabilities.Any(c => c.Tags.Any(t => def.Tags.Contains(t)))
        );
        return ValueTask.FromResult<IActionExecutor?>(match);
    }
}

// === Engine ===
public sealed class ActionEngine {
    private readonly IActionExecutorRegistry _registry;
    private readonly List<IActionPipelineStage> _pipeline;

    public async ValueTask<ExecutionResult> ExecuteAsync(ActionDef def, ActionContext ctx, CancellationToken ct) {
        var executor = await _registry.ResolveAsync(def, ct);
        if (executor is null) return ExecutionResult.Failed("No executor found");

        foreach (var stage in _pipeline.Where(s => s.BeforeAsync != null)) {
            var sr = await stage.BeforeAsync(def, ctx, ct);
            if (!sr.Continue) return ExecutionResult.Failed(sr.Reason);
        }

        var result = await executor.ExecuteAsync(def, ctx, ct);

        foreach (var stage in _pipeline.Where(s => s.AfterAsync != null).Reverse()) {
            await stage.AfterAsync(def, result, ctx, ct);
        }

        return result;
    }
}
```

### 3.4 Context Engine

```csharp
public interface IContextEngine {
    ValueTask<ApplicationContext> DetectAsync(CancellationToken ct);
    event EventHandler<ApplicationContext> ContextChanged;
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

public sealed record ApplicationContext(
    string ProcessName,
    string WindowTitle,
    nint WindowHandle,
    DateTimeOffset Timestamp
);

// Win32 实现
public sealed class Win32ContextEngine : IContextEngine {
    private CancellationTokenSource? _cts;
    private ApplicationContext _last;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(500);

    public event EventHandler<ApplicationContext>? ContextChanged;

    public Task StartAsync(CancellationToken ct) {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => PollLoop(_cts.Token));
        return Task.CompletedTask;
    }

    private async Task PollLoop(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            var current = DetectOnce();
            if (current.ProcessName != _last.ProcessName || current.WindowTitle != _last.WindowTitle) {
                _last = current;
                ContextChanged?.Invoke(this, current);
            }
            await Task.Delay(_pollInterval, ct);
        }
    }

    private ApplicationContext DetectOnce() {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);
        var processName = GetProcessNameByPid(pid);
        var title = GetWindowTitle(hwnd);
        return new ApplicationContext(processName, title, hwnd, DateTimeOffset.UtcNow);
    }
}

// Profile Resolver
public interface IProfileResolver {
    Profile? ResolveActiveProfile(ApplicationContext ctx, IReadOnlyList<Profile> allProfiles);
}

public sealed class PriorityBasedProfileResolver : IProfileResolver {
    public Profile? ResolveActiveProfile(ApplicationContext ctx, IReadOnlyList<Profile> allProfiles) {
        return allProfiles
            .SelectMany(p => p.ContextRules.Select(r => (Profile: p, Rule: r)))
            .Where(pr => Matches(pr.Rule.Matcher, ctx))
            .OrderByDescending(pr => pr.Rule.Priority)
            .Select(pr => pr.Profile)
            .FirstOrDefault() ?? /* Default Profile */;
    }

    private bool Matches(ContextMatcher matcher, ApplicationContext ctx) {
        if (matcher.ProcessName is { } pn) {
            if (pn.Equals != null && !string.Equals(pn.Equals, ctx.ProcessName, StringComparison.OrdinalIgnoreCase)) return false;
            if (pn.Matches != null && !Regex.IsMatch(ctx.ProcessName, pn.Matches, RegexOptions.IgnoreCase)) return false;
        }
        if (matcher.WindowTitle is { } wt) {
            if (wt.Contains != null && !ctx.WindowTitle.Contains(wt.Contains, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
```

---

## 4. Data Architecture

### 4.1 Profile Schema（完整版）

```json
{
  "$schema": "https://flowring.dev/schema/profile-v1.json",
  "type": "object",
  "required": ["metadata", "ringGraph", "actionRefs", "contextRules", "version"],
  "properties": {
    "metadata": {
      "type": "object",
      "required": ["id", "name", "version", "schemaVersion", "createdAt", "updatedAt", "checksum"],
      "properties": {
        "id":            { "type": "string", "pattern": "^[a-z0-9-]{3,64}$" },
        "name":          { "type": "string", "minLength": 1, "maxLength": 64 },
        "version":       { "type": "string", "pattern": "^[0-9]+\\.[0-9]+\\.[0-9]+$" },
        "schemaVersion": { "type": "string", "pattern": "^[0-9]+\\.[0-9]+$" },
        "createdAt":     { "type": "string", "format": "date-time" },
        "updatedAt":     { "type": "string", "format": "date-time" },
        "checksum":      { "type": "string", "pattern": "^[a-f0-9]{64}$" },
        "description":   { "type": "string", "maxLength": 500 },
        "tags":          { "type": "array", "items": { "type": "string" }, "maxItems": 20 }
      }
    },
    "ringGraph": { "$ref": "#/$defs/ringGraph" },
    "actionRefs": {
      "type": "array",
      "items": { "type": "string" },
      "description": "ActionDef IDs referenced by this profile"
    },
    "contextRules": {
      "type": "array",
      "items": { "$ref": "#/$defs/contextRule" }
    },
    "pluginConfig": { "type": "object", "additionalProperties": true },
    "aiSettings": {
      "type": "object",
      "properties": {
        "defaultModel": { "type": "string" },
        "promptTemplates": { "type": "object" }
      }
    }
  },
  "$defs": {
    "ringGraph": {
      "type": "object",
      "required": ["rootId", "nodes"],
      "properties": {
        "rootId": { "type": "string" },
        "nodes": {
          "type": "object",
          "additionalProperties": { "$ref": "#/$defs/ringNode" }
        }
      }
    },
    "ringNode": {
      "type": "object",
      "required": ["id", "depth", "displayName", "slots"],
      "properties": {
        "id":          { "type": "string" },
        "parentId":    { "type": ["string", "null"] },
        "depth":       { "type": "integer", "minimum": 1, "maximum": 3 },
        "displayName": { "type": "string" },
        "slots": {
          "type": "object",
          "propertyNames": ["Top","TopRight","Right","BottomRight","Bottom","BottomLeft","Left","TopLeft"],
          "additionalProperties": { "$ref": "#/$defs/slot" }
        }
      }
    },
    "slot": {
      "oneOf": [
        { "type": "object", "required": ["kind"], "properties": { "kind": { "const": "empty" } } },
        {
          "type": "object",
          "required": ["kind", "actionRef"],
          "properties": { "kind": { "const": "action" }, "actionRef": { "type": "string" } }
        },
        {
          "type": "object",
          "required": ["kind", "childRingId"],
          "properties": { "kind": { "const": "childRing" }, "childRingId": { "type": "string" } }
        },
        {
          "type": "object",
          "required": ["kind", "actionRefs"],
          "properties": {
            "kind": { "const": "group" },
            "actionRefs": { "type": "array", "items": { "type": "string" }, "maxItems": 8 }
          }
        }
      ]
    },
    "contextRule": {
      "type": "object",
      "required": ["matcher", "priority"],
      "properties": {
        "matcher": {
          "type": "object",
          "properties": {
            "processName": {
              "type": "object",
              "properties": {
                "equals":  { "type": "string" },
                "matches": { "type": "string" }
              },
              "additionalProperties": false
            },
            "windowTitle": {
              "type": "object",
              "properties": { "contains": { "type": "string" } },
              "additionalProperties": false
            },
            "appId": { "type": "string" }
          }
        },
        "priority": { "type": "integer", "minimum": 0, "maximum": 1000 }
      }
    }
  }
}
```

### 4.2 Action Schema

```json
{
  "$schema": "https://flowring.dev/schema/action-v1.json",
  "type": "object",
  "required": ["id", "kind", "displayName", "version", "permission"],
  "properties": {
    "id":          { "type": "string", "pattern": "^[a-z0-9.-]{3,128}$" },
    "kind":        { "enum": ["keyboard", "system", "application", "ai", "workflow"] },
    "displayName": { "type": "string", "minLength": 1, "maxLength": 64 },
    "iconHint":    { "type": "string" },
    "hotkey":      { "type": "string" },
    "version":     { "type": "string", "pattern": "^[0-9]+\\.[0-9]+\\.[0-9]+$" },
    "permission":  { "enum": ["safe", "normal", "dangerous"] },
    "tags":        { "type": "array", "items": { "type": "string" }, "maxItems": 20 },
    "capabilities":{
      "type": "array",
      "items": { "type": "string" },
      "description": "Capability IDs this action requires"
    },
    "payload": {
      "oneOf": [
        { "$ref": "#/$defs/keyboardPayload" },
        { "$ref": "#/$defs/systemPayload" },
        { "$ref": "#/$defs/applicationPayload" },
        { "$ref": "#/$defs/aiPayload" },
        { "$ref": "#/$defs/workflowPayload" }
      ]
    }
  },
  "$defs": {
    "keyboardPayload": {
      "type": "object",
      "required": ["keys"],
      "properties": {
        "keys":      { "type": "string" },
        "modifiers": { "type": "array", "items": { "enum": ["Ctrl","Shift","Alt","Win","Meta"] } }
      }
    },
    "systemPayload": {
      "type": "object",
      "required": ["op"],
      "properties": {
        "op":   { "enum": ["screenshot","adjust-volume","window-minimize","window-maximize","lock-screen","empty-recycle-bin"] },
        "args": { "type": "object" }
      }
    },
    "applicationPayload": {
      "type": "object",
      "required": ["processName", "command"],
      "properties": {
        "processName": { "type": "string" },
        "command":     { "type": "string" },
        "args":        { "type": "array", "items": { "type": "string" } }
      }
    },
    "aiPayload": {
      "type": "object",
      "required": ["promptTemplate", "model"],
      "properties": {
        "promptTemplate": { "type": "string" },
        "model": {
          "type": "object",
          "required": ["provider", "name"],
          "properties": {
            "provider": { "enum": ["openai","anthropic","local","custom"] },
            "name":     { "type": "string" },
            "params":   { "type": "object" }
          }
        }
      }
    },
    "workflowPayload": {
      "type": "object",
      "required": ["steps"],
      "properties": {
        "steps": {
          "type": "array",
          "minItems": 1,
          "items": { "type": "string", "description": "Action ID" }
        },
        "failureStrategy": { "enum": ["abort","continue","prompt"], "default": "abort" }
      }
    }
  }
}
```

### 4.3 Flow Code Schema（同步版）

```json
{
  "$schema": "https://flowring.dev/schema/flow-code-v2.json",
  "type": "object",
  "required": ["format", "version", "vector", "payload", "checksum"],
  "properties": {
    "format":      { "const": "FLOW-CODE" },
    "version":     { "type": "string", "pattern": "^[0-9]+\\.[0-9]+$" },
    "snapshotId":  { "type": "string" },
    "vector": {
      "type": "object",
      "description": "Per-device clock map",
      "additionalProperties": { "type": "integer", "minimum": 0 }
    },
    "payload": {
      "type": "object",
      "required": ["profile", "actions"],
      "properties": {
        "profile": { "$ref": "profile-v1.json" },
        "actions": { "type": "array", "items": { "$ref": "action-v1.json" } }
      }
    },
    "delta": {
      "type": "object",
      "description": "Optional incremental update",
      "properties": {
        "baseSnapshotId": { "type": "string" },
        "patches": { "type": "array", "items": { "$ref": "#/$defs/jsonPatch" } }
      }
    },
    "checksum": { "type": "string", "pattern": "^[a-f0-9]{64}$" },
    "encryption": {
      "type": "object",
      "required": ["algorithm", "kdf", "salt", "nonce"],
      "properties": {
        "algorithm": { "const": "AES-256-GCM" },
        "kdf":       { "enum": ["PBKDF2-SHA256","Argon2id"] },
        "iterations":{ "type": "integer", "minimum": 100000 },
        "salt":      { "type": "string" },
        "nonce":     { "type": "string" },
        "authTag":   { "type": "string" }
      }
    },
    "senderDeviceId": { "type": "string" },
    "pairingCode":    { "type": "string", "pattern": "^[0-9]{6}$" }
  },
  "$defs": {
    "jsonPatch": {
      "type": "object",
      "required": ["op", "path"],
      "properties": {
        "op":    { "enum": ["add","remove","replace","move","copy","test"] },
        "path":  { "type": "string" },
        "value": {},
        "from":  { "type": "string" }
      }
    }
  }
}
```

---

## 5. Runtime Event Pipeline

完整 11 阶段流程（强化版）：

```
[USER] Long-press Side Button
  │
  ▼
[1] Raw Capture (Win32 Hook)
    WH_MOUSE_LL → MSLLHOOKSTRUCT
    Latency target: < 1ms
    │
    ▼
[2] Input Normalization
    MouseAdapter.RawToSpatialIntent()
    Output: SpatialIntentEvent { TriggerType.Mouse, OriginPoint=(x,y) }
    Latency target: < 1ms
    │
    ▼
[3] State Machine Transition
    Idle → Pressed → HoldDetected → RingOpening
    Latency budget cumulative: < 150ms
    │
    ▼
[4] Context Query
    Win32ContextEngine.DetectAsync()
    → GetForegroundWindow → PID → ProcessName
    Output: ApplicationContext { "Code.exe", "...", hwnd, ts }
    Latency target: < 5ms (cached: < 1ms)
    │
    ▼
[5] Profile Resolve
    PriorityBasedProfileResolver.ResolveActiveProfile(ctx, allProfiles)
    Output: ActiveProfile { id: "developer", rootRingId: "dev-root" }
    Latency target: < 5ms
    │
    ▼
[6] Profile Lazy Load
    ProfileStore.LoadAsync(profileId)
    - Memory cache hit: < 1ms
    - Cold load: < 50ms
    Output: Profile (with RingGraph)
    │
    ▼
[7] Render Ring (Bridge → WebView2)
    BridgeServer.PostMessage(RING_OPEN, { ringTree, deadZonePx })
    React renders RuntimeRing component
    Latency target: < 50ms (first frame)
    │
    ▼
[USER] Move Mouse
    │
    ▼
[8] Direction Selection Loop (60fps)
    mousemove → Hook → Adapter → DirectionResolver
    - distance < 30px: Direction.Center (Dead Zone)
    - distance ≥ 30px: compute nearest 8-direction
    BridgeServer.PostMessage(RING_HIGHLIGHT, { direction })
    Latency target per frame: < 16ms
    │
    ▼
[USER] Release Button
    │
    ▼
[9] State Transition + Action Resolution
    Selecting → Executing
    RingResolver.Resolve(currentRing, selectedDirection)
    Output: ActionTriggered(actionDef) | EnterChildRing(childRing) | Cancelled
    Latency target: < 5ms
    │
    ▼
[10] Permission Check (Pipeline Stage)
    PermissionCheckStage.BeforeAsync()
    - Safe Action: skip user prompt
    - Normal Action: skip user prompt (audit log)
    - Dangerous Action: show consent dialog (v1.1)
    Latency target: < 5ms
    │
    ▼
[11] Action Execution (Pipeline → Executor)
    PermissionCheck → ContextInject → Executor.ExecuteAsync → AuditLog
    Output: ExecutionResult { success, error?, durationMs }
    Latency target: < 30ms (sync) | < 100ms (async kickoff)
    │
    ▼
[12] Close + Log
    BridgeServer.PostMessage(RING_CLOSE)
    LogStore.Append(ExecutionLog { ts, profile, action, status, durationMs })
    State Machine: Executing → Closing → Idle
    Latency target: < 50ms
    │
    ▼
[USER] Sees effect (e.g., paste happened)
```

**总时延预算**：按下 → 看到 Ring = **< 200ms**；释放 → Action 执行完成 = **< 50ms**（同步 Action）。

---

## 6. Performance Architecture（P95 / P99 目标）

### 6.1 延迟目标

| 阶段 | P50 | P95 | P99 | 硬上限 |
|---|---|---|---|---|
| **Capture** (Hook → RawInputEvent) | < 0.5ms | < 1ms | < 2ms | 5ms |
| **Normalize** (RawInput → SpatialIntentEvent) | < 0.3ms | < 1ms | < 2ms | 2ms |
| **HoldDetected** (按下到识别长按) | 150ms | 150ms | 180ms | 200ms |
| **Context Query** | < 1ms (缓存命中) | < 5ms | < 10ms | 10ms |
| **Profile Resolve** | < 1ms | < 5ms | < 10ms | 10ms |
| **Profile Load** (内存缓存) | < 0.5ms | < 1ms | < 3ms | 3ms |
| **Profile Load** (冷) | 30ms | 50ms | 100ms | 100ms |
| **Render 首帧** (Bridge → React) | 30ms | 50ms | 80ms | 100ms |
| **Direction Update** (每帧) | 8ms | 16ms | 20ms | 20ms |
| **Action Execute** (同步，Keyboard) | 5ms | 30ms | 100ms | 100ms |
| **Action Execute** (同步，System Screenshot) | 50ms | 100ms | 200ms | 200ms |
| **Ring Close + Log** | 5ms | 20ms | 50ms | 50ms |

### 6.2 端到端目标

| 用户感知 | P50 | P95 | P99 |
|---|---|---|---|
| **按下到看到 Ring** | 180ms | 200ms | 250ms |
| **释放到 Action 执行** | 30ms | 50ms | 100ms |

### 6.3 资源消耗目标

| 指标 | 目标 |
|---|---|
| **冷启动时间** | < 1.5s |
| **空闲内存 (无 Ring 显示)** | < 80MB |
| **Ring 显示时内存峰值** | < 200MB |
| **CPU 空闲占用** | < 1% |
| **Hook CPU 占用** | < 0.5% |
| **Storage 占用 (典型用户)** | < 5MB |

### 6.4 性能预算分配原则

- **Hook 路径不能分配堆内存**：用 struct 传递，避免 GC。
- **mousemove 必须批处理**：合并 4ms 内的多次移动，只发一次 Direction Update。
- **Context 缓存 TTL = 500ms**：避免每次按下都查 Win32。
- **Profile 内存常驻**：metadata 索引常驻，Ring 树懒加载。
- **Pipeline Stage 零分配**：所有 Stage 的输入输出用 readonly record struct。

### 6.5 性能测试要求

每个 Sprint 必须包含：

- **Micro-benchmark**：BenchmarkDotNet 测核心算法（方向判定、状态机转换）。
- **Macro-benchmark**：用脚本生成 1000 次模拟按键 → 测量端到端时延分布。
- **Stress test**：1000 个 slot、50 个 Profile、并发触发。
- **Memory profiling**：dotMemory / PerfView，确认无泄漏。

---

## 7. Security Architecture

### 7.1 Permission Tier（Action 三级分类）

#### Safe Action

**定义**：纯本地、不可逆性低、不涉及 OS 核心状态。

**示例**：
- 复制（Ctrl+C）
- 粘贴（Ctrl+V）
- 撤销（Ctrl+Z）
- 切换窗口（Alt+Tab）
- 移动光标

**策略**：
- 无需用户确认
- 不写入审计日志（仅写入统计）
- 可被任何 Profile 自动调用

#### Normal Action

**定义**：涉及 OS 状态、可影响他人可见行为、有一定不可逆性。

**示例**：
- 截图
- 音量调整
- 启动应用程序
- 关闭窗口

**策略**：
- 无需用户确认（每次按下即执行）
- 写入审计日志（用于回顾）
- Profile 必须显式声明才可使用

#### Dangerous Action

**定义**：可丢失数据、可被远端观察、不可撤销。

**示例**：
- 删除文件
- 执行任意命令
- 发送网络请求
- 系统级修改（注册表、UAC 操作）
- AI 调用（可能产生费用、可能泄露数据）

**策略**：
- **首次使用**弹窗：用户必须显式同意
- **每次执行**前可选二次确认
- **强制审计日志**
- **Profile 必须显式声明 + 用户授权**

### 7.2 Permission Middleware（Pipeline Stage）

```csharp
public sealed class PermissionCheckStage : IActionPipelineStage {
    private readonly IConsentStore _consentStore;
    private readonly IDialogService _dialog;

    public string Name => "permission-check";

    public async ValueTask<StageResult> BeforeAsync(ActionDef def, ActionContext ctx, CancellationToken ct) {
        var tier = def.Permission;

        if (tier == PermissionTier.Safe) return StageResult.Continue;

        if (tier == PermissionTier.Normal) {
            if (!ctx.ActiveProfile.ActionRefs.Contains(def.Id))
                return StageResult.Abort("Action not in profile");
            return StageResult.Continue;
        }

        if (tier == PermissionTier.Dangerous) {
            var granted = await _consentStore.IsGrantedAsync(def.Id, ct);
            if (!granted) {
                var userConsent = await _dialog.ShowConsentAsync(def, ct);
                if (!userConsent.Granted) return StageResult.Abort("User denied");
                await _consentStore.GrantAsync(def.Id, userConsent.ExpiresAt, ct);
            }
            return StageResult.Continue;
        }

        return StageResult.Continue;
    }

    public ValueTask<StageResult> AfterAsync(...) => ValueTask.FromResult(StageResult.Continue);
}
```

### 7.3 Plugin Security（三层防御）

#### 第一层：进程隔离

| 沙箱等级 | 适用场景 | 实现 |
|---|---|---|
| **进程内（默认）** | 受信任插件 | AssemblyLoadContext + 受限 API |
| **进程外** | 需要 HTTP/网络的插件 | 独立进程 + Named Pipe |
| **WASM（v1.1）** | 不可信第三方 | Wasmer + 完全沙箱 |

#### 第二层：API Surface 限制

- 插件**只能**调用 IPluginHost 暴露的方法
- 不允许 `System.IO.File`、`System.Net.Http.*` 等危险 API 直接调用
- 必须通过 PluginContext 提供的包装 API（带权限校验）

#### 第三层：资源限制

| 资源 | 限制 |
|---|---|
| 内存 | 100MB |
| CPU | 5%（持续） |
| 存储 | 10MB |
| Context 提供频率 | 1Hz |
| Action 执行频率 | 10/min |
| 网络 | 默认禁止 |

### 7.4 Flow Code 安全

- **端到端加密**：用户密码 → Argon2id → AES-256-GCM key
- **零知识**：Sync Server 永远看不到明文
- **设备配对**：6 位配对码 + device signature
- **撤销机制**：用户可随时撤销设备的同步权限
- **过期机制**：Flow Code 可设过期时间（默认 30 天）

### 7.5 反滥用

- **节流**：同一 Action 1 秒内最多触发 10 次
- **白名单/黑名单**：可配置禁用某些 Action
- **紧急停用**：双击 Esc 全局禁用 5 秒
- **审计追溯**：所有 Dangerous Action 永久记录

---

## 8. MVP Development Boundary

### 8.1 第一版本必须实现（In Scope）

```
✅ Mouse Trigger
   - Side button (XBUTTON1 / XBUTTON2)
   - Optional: Right button long press
   - Hold threshold: 150ms

✅ Ring Display
   - 8-direction with Dead Zone
   - Glass UI visual
   - Fade-in/out animation (200ms / 150ms)
   - Center point at trigger location

✅ Direction Selection
   - Geometric direction resolver
   - 30px dead zone (configurable)
   - Highlight on direction change
   - 60fps selection loop

✅ Keyboard Action
   - SendInput-based keystroke injection
   - Modifier support (Ctrl/Shift/Alt/Win)
   - Multi-key sequence

✅ System Action (limited set)
   - Screenshot (full screen)
   - Volume up/down/mute
   - Window minimize

✅ Profile Save
   - JSON-based profile storage
   - One default profile on first launch
   - Manual save/load

✅ Context Engine (basic)
   - Win32 GetForegroundWindow
   - Process name matching
   - Default Profile fallback

✅ Flow Code (basic)
   - Export (no encryption for MVP, but with checksum)
   - Import with version check
   - One-device flow (no multi-device sync)

✅ Ring Studio (minimal)
   - 3-pane layout
   - Drag from Action Library to slot
   - Live preview
   - Save
   - No undo/redo in MVP
```

### 8.2 第一版本明确不做（Out of Scope）

```
❌ AI Action
❌ Plugin Marketplace
❌ Cloud Sync (only local Flow Code export/import)
❌ Workflow Action (composite)
❌ Touch pen support
❌ Custom keyboard trigger
❌ Advanced automation (loops, conditions)
❌ macOS / Linux support
❌ Telemetry / Crash reporting
❌ Theme customization
❌ Animation customization
❌ Plugin system (架构预留但不实现)
```

### 8.3 MVP 验收标准

| 维度 | 验收 |
|---|---|
| **功能** | 上述 MVP 列表全部实现 |
| **性能** | P95 Ring 打开 < 200ms |
| **稳定性** | 连续运行 24 小时无崩溃 |
| **兼容性** | Win10 21H2+ / Win11 全版本 |
| **安装** | Squirrel 打包，安装 < 30s |
| **反病毒** | VirusTotal 主流杀软全绿 |
| **用户体验** | 新用户 30 秒内能完成第一次 Action 执行 |

---

## 9. Future Evolution

### 9.1 v1.1 路线

```
v1.1 Focus: 智能化 + 扩展性
━━━━━━━━━━━━━━━━━━━━━━━━━━

• AI Action (异步执行)
  - OpenAI / Anthropic / Local LLM 集成
  - Streaming UI in Runtime Ring
  - Context-aware prompts (cursor position, selected text, app context)

• Plugin SDK GA
  - Public Plugin SDK NuGet package
  - Sample plugins (Obsidian, Spotify, VS Code)
  - Plugin Marketplace (curated, manually reviewed)

• Flow Code Sync
  - Multi-device sync via user's Sync Server (or self-hosted)
  - E2EE with Argon2id
  - Conflict resolution UI

• Touch Pen Support
  - PenAdapter (pressure → pressure field)
  - Tilt-aware direction resolver

• Workflow Action
  - Composite actions with failure strategies
  - Visual workflow editor in Studio

• Telemetry (opt-in)
  - Sentry SDK for crash reporting
  - Anonymous usage metrics
  - Performance telemetry
```

### 9.2 v2.0 路线

```
v2.0 Focus: 跨平台 + 协作
━━━━━━━━━━━━━━━━━━━━━━━━

• macOS Support
  - MacDesktopBridge (Quartz Event Services)
  - NSWorkspace for context detection
  - Cocoa UI executor
  - Architecture unchanged: RingCore / RingUI 一行不改

• Cross-Platform Profile Sync
  - Profile portability across Win/Mac/Linux
  - Platform-specific Action tagged with `platform` field
  - Auto-skip non-applicable Actions

• AI Context Assistant
  - Proactive Profile suggestion based on usage patterns
  - Smart Conflict Resolver (AI suggests which version to keep)
  - Auto-generate Profile from user's hotkey usage history

• Collaborative Profiles
  - Team-shared Profiles (read-only)
  - Profile Marketplace
  - Rating & review system
```

### 9.3 未来方向（v3.0+）

```
v3.0+ Vision: 智能交互 OS
━━━━━━━━━━━━━━━━━━━━━━━━━

• Spatial AR Overlay
  - Vision Pro / Quest 3 spatial rendering
  - 3D Ring in user's environment
  - Gaze + gesture input

• Predictive Intent Engine
  - ML model predicts next likely action
  - Pre-highlight suggested direction
  - Reduces Ring open frequency

• Cross-Application Workflows
  - "Research a topic" = open browser + take notes + save to Obsidian
  - LLM-orchestrated multi-app automation
  - User-defined natural language triggers

• Community Plugin Ecosystem
  - Public marketplace
  - Plugin analytics for authors
  - Revenue share for premium plugins
```

### 9.4 演进的核心约束

无论版本如何演进，以下架构原则**永远不变**：

1. **Ring = Graph Node**（不是菜单）
2. **Action = ActionStep + Executor**（Capability-based）
3. **Context Engine 永远独立**
4. **RingStudio 是独立模块**
5. **Plugin 必须沙箱**
6. **Profile 必须有版本和快照**
7. **Flow Code 必须加密**
8. **OS Bridge 永远抽象**

---

## 附录 A：版本兼容性矩阵

| 引擎版本 | Profile Schema v1 | Action Schema v1 | FlowCode v1 | FlowCode v2 |
|---|---|---|---|---|
| v1.0 | ✅ 读写 | ✅ 读写 | ✅ 读写 | ❌ |
| v1.1 | ✅ 读 / 写新增字段 | ✅ 读写 | ✅ 读写 | ✅ 读写 |
| v2.0 | ⚠️ 仅读（提示升级） | ✅ 读写 | ⚠️ 仅读 | ✅ 读写 |

## 附录 B：ADR 索引

- ADR-001：UI 用 React + WebView2
- ADR-002：Desktop Core 用 C# / .NET 8
- ADR-003：进程模型单进程
- ADR-004：通信用 WebMessage + Named Pipe
- ADR-005：持久化用分文件 JSON
- ADR-006：Context 检测用 Win32 API
- ADR-007：Profile 用 JSON Schema
- ADR-008：Flow Code 用 AES-256-GCM
- **ADR-009（新增）**：Action Engine 用 Capability-based 设计
- **ADR-010（新增）**：Studio 是独立模块 + Document Service
- **ADR-011（新增）**：Plugin 用三层沙箱（进程内 / 进程外 / WASM）

## 附录 C：与 v1.0-architecture.md 的关系

本文档是 [v1.0-architecture.md](v1.0-architecture.md) 的**深度扩展版**，新增：

- Q1-Q5 深度架构验证的答案
- 七层分层模型（v1.0 是 5 层）
- Capability-based Action Engine（v1.0 是按 Kind 硬编码）
- Studio 独立模块设计（v1.0 是嵌入应用）
- Plugin 架构完整设计（v1.0 是预留）
- Permission Middleware 三级分类
- Flow Code 同步、合并、加密完整方案
- 性能 P95/P99 目标（v1.0 是单点目标）

**两份文档并存**：
- `v1.0-architecture.md` 是基线（产品/团队理解用）
- `technical-architecture-blueprint.md` 是深度版（工程实施用）

---

**文档版本**：v1.0 Technical Architecture Blueprint
**最后更新**：2026-10-02
**下一阶段**：用户确认 → 进入 Sprint 1（RingCore 基础）
**配套文档**：`docs/specs/ring-core-spec.md` / `docs/specs/action-engine-spec.md` / `docs/specs/studio-spec.md` / `docs/specs/plugin-spec.md`（按 Sprint 进度输出）
