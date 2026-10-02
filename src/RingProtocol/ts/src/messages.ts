// RingProtocol: TS 端消息类型定义
// C# 端为权威来源，本文件由 .NET 反向生成（Phase 5 spike）。
// MVP 阶段手填，Phase 5 接入工具链后由脚本生成。

export const PROTOCOL_VERSION = 1;
export const MIN_SUPPORTED_VERSION = 1;

export type Direction =
  | 'Center'
  | 'Top'
  | 'TopRight'
  | 'Right'
  | 'BottomRight'
  | 'Bottom'
  | 'BottomLeft'
  | 'Left'
  | 'TopLeft';

export interface RingPoint {
  X: number;
  Y: number;
}

export interface ProtocolEnvelope<T> {
  type: string;
  v: number;
  payload: T;
}

// C# → TS
export interface RingOpenPayload {
  originPoint: RingPoint | null;
  ringTree: string;
  deadZonePx: number;
  activeProfileId: string;
}

export interface RingHighlightPayload {
  direction: Direction;
}

export interface RingClosePayload {
  reason: string;
}

export interface ProfileListEntry {
  id: string;
  name: string;
  isDefault: boolean;
}

export interface ProfileListPayload {
  profiles: ProfileListEntry[];
}

export interface RingStudioLoadPayload {
  profileId: string;
  profileJson: string;
  ringGraphJson: string;
  actionLibrary: string[];
}

// TS → C#
export interface RingDirectionLockPayload {
  direction: Direction;
}

export interface RingTriggerPayload {
  triggerType: string;
  modifiers: number;
}

export interface ActionPreviewPayload {
  actionId: string;
}

export interface StudioSavePayload {
  profileId: string;
  profileJson: string;
  ringGraphJson: string;
}

export interface FlowCodeExportPayload {
  profileId: string;
  encrypt: boolean;
}

export interface FlowCodeImportPayload {
  code: string;
  passphrase: string | null;
}