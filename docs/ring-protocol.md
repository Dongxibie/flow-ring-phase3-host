# Flow Ring Protocol 规范
Host（C# DesktopHost） 与 WebView2（TS RingUI） 的双向协议定义。

> 这是 C# 与 TS 的唯一共享定义——禁止在两侧手抄类型。
> 版本：`ProtocolVersion.Current = 1`
> 不兼容协议直接拒绝（Phase 3 接入版本协商）。

---

## 1. 协议版本

- `v` 字段：当前实现版本 `ProtocolVersion.Current = 1`
- 不兼容时直接拒绝整条消息并打点 ERR_PROTOCOL_VERSION

---

## 2. 消息信封

每条跨进程消息的 JSON 结构：

```json
{
  "type": "RING_OPEN",
  "v": 1,
  "payload": { ... }
}
```

字段：
- `type`：消息名（C# / TS 两侧枚举对齐）
- `v`：协议版本号
- `payload`：消息负载对象（每种消息不同）

---

## 3. 消息清单

### 3.1 C# → TS（Host → UI）

| Type | Payload | 触发时机 |
|---|---|---|
| `RING_OPEN` | `RingOpenPayload` | 状态机转入 `Selecting`，UI 显示环形菜单 |
| `RING_HIGHLIGHT` | `RingHighlightPayload` | mousemove 触发方向变更 |
| `RING_CLOSE` | `RingClosePayload` | 用户松手 / Dead Zone 命中 / 错误取消 |
| `PROFILE_LIST` | `ProfileListPayload` | Profile Manager 页面拉取列表 |
| `RING_STUDIO_LOAD` | `RingStudioLoadPayload` | Studio 页面进入编辑模式 |

### 3.2 TS → C#（UI → Host）

| Type | Payload | 触发时机 |
|---|---|---|
| `RING_DIRECTION_LOCK` | `RingDirectionLockPayload` | 用户移动鼠标过 Dead Zone，UI 锁定方向 |
| `RING_TRIGGER` | `RingTriggerPayload` | 用户主动触发（设置页改触发键时通知 host） |
| `ACTION_PREVIEW` | `ActionPreviewPayload` | Studio / Manager 鼠标悬停 Action 卡片 |
| `STUDIO_SAVE` | `StudioSavePayload` | Studio 保存按钮 |
| `FLOW_CODE_EXPORT` | `FlowCodeExportPayload` | Profile Manager / Settings 触发导出 |
| `FLOW_CODE_IMPORT` | `FlowCodeImportPayload` | Settings 触发导入 |

---

## 4. Payload Schema

### 4.1 C# → TS

```typescript
interface RingPoint { X: number; Y: number; }
interface RingOpenPayload {
  originPoint: RingPoint | null;   // 鼠标按下点；HotKey 触发时为 null
  ringTree: string;                // RingNode 图 JSON 字符串
  deadZonePx: number;              // Dead Zone 半径（默认 30）
  activeProfileId: string;
}
interface RingHighlightPayload {
  direction: 'Top' | 'TopRight' | 'Right' | 'BottomRight'
           | 'Bottom' | 'BottomLeft' | 'Left' | 'TopLeft' | 'Center';
}
interface RingClosePayload { reason: string; }
interface ProfileListEntry { id: string; name: string; isDefault: boolean; }
interface ProfileListPayload { profiles: ProfileListEntry[]; }
interface RingStudioLoadPayload {
  profileId: string;
  profileJson: string;            // Profile 整体 JSON 字符串
  ringGraphJson: string;           // RingNode 图 JSON 字符串
  actionLibrary: string[];        // 可用 ActionDef.Id 列表
}
```

### 4.2 TS → C#

```typescript
interface RingDirectionLockPayload {
  direction: 'Top' | 'TopRight' | 'Right' | 'BottomRight'
           | 'Bottom' | 'BottomLeft' | 'Left' | 'TopLeft' | 'Center';
}
interface RingTriggerPayload {
  triggerType: 'MouseSideButton' | 'MiddleButton' | 'RightButtonLongPress' | 'HotKey';
  modifiers: number;              // bitfield: Shift=1, Ctrl=2, Alt=4, Win=8
}
interface ActionPreviewPayload { actionId: string; }
interface StudioSavePayload {
  profileId: string;
  profileJson: string;
  ringGraphJson: string;
}
interface FlowCodeExportPayload { profileId: string; encrypt: boolean; }
interface FlowCodeImportPayload { code: string; passphrase: string | null; }
```

---

## 5. 坐标系

- 单位 = 逻辑像素（WebView2 渲染坐标）
- Y 轴向下为正
- WebView2 侧通过 `window.devicePixelRatio` 把物理像素折算成逻辑像素后再发

---

## 6. 通道分流

- **低频消息**（< 5 Hz）：WebMessage（WebView2 → Host / Host → WebView2）
- **高频 mousemove**（60 fps）：Named Pipe（Host 内部 InputAdapter → RingEngine，避免阻塞 UI 线程）

详见 `docs/architecture/v1.0-architecture.md` §1.2 关键架构决策第 4 行。

---

## 7. 版本演进

- 不破坏现有 `type` 字段
- 新增可选字段通过 `payload.<新字段>` 加，TS 端走 optional property
- 不兼容变更必须升 `v` 字段并双端同时升级

---

## 8. 落地位置

- C#：`src/RingProtocol/Messages.cs`
- TS：`src/RingProtocol/ts/src/messages.ts`（csproj 编译时通过 `None Include` 复制到输出目录，Phase 5 起通过工具链反向生成）

> Phase 2 阶段两处均为手动维护；Phase 5 spike 完成后接入 JSON Schema → C#/TS 类型生成器，禁止手抄。