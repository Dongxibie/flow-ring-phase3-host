using System.ComponentModel;
using System.Runtime.InteropServices;
using FlowRing.DesktopBridge.Abstractions;
using FlowRing.RingCore;
using FlowRing.RingCore.Geometry;
using Microsoft.Extensions.Logging;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// Win32 WH_MOUSE_LL Hook 真实实现。
/// 识别鼠标侧键（XBUTTON1 / XBUTTON2）、中键、右键长按；其他按键 pass-through 给 CallNextHookEx。
/// 长按识别：按下 → 启动计时器 → 超过 150ms 触发 HoldDetected 事件。
/// </summary>
public sealed class MouseInputAdapter : IInputAdapter, IDisposable
{
    private const int HoldThresholdMs = 150;
    private const long TriggerWindowMs = 600; // 长按后再次按下若超过此间隔则视为新一次触发
    private static readonly TimeSpan HoldRepeatTimeout = TimeSpan.FromMilliseconds(TriggerWindowMs);

    private readonly ILogger<MouseInputAdapter> _logger;
    private readonly InputAdapterEvents _events = new();
    private delegate nint HookProc(int nCode, nint wParam, nint lParam);
    private readonly HookProc _hookProcDelegate;
    private nint _hookHandle;
    private nint _moduleHandle;
    private readonly CancellationTokenSource _cts = new();

    // 触发态
    private int _pressedButtonVk;
    private NativeMethods.POINT _pressPoint;
    private long _pressTimestampMs;
    private bool _holdFired;
    private DateTimeOffset _lastReleaseAt = DateTimeOffset.MinValue;

    public MouseInputAdapter(ILogger<MouseInputAdapter> logger)
    {
        _logger = logger;
        _hookProcDelegate = HookCallback;
    }

    public event EventHandler<SpatialIntentEvent>? IntentEmitted
    {
        add => _events.IntentEmitted += value;
        remove => _events.IntentEmitted -= value;
    }

    public event EventHandler<RawInputEvent>? RawInputEmitted
    {
        add => _events.RawInputEmitted += value;
        remove => _events.RawInputEmitted -= value;
    }

    public async Task InstallAsync(CancellationToken ct)
    {
        if (_hookHandle != nint.Zero)
        {
            return;
        }

        _moduleHandle = NativeMethods.GetModuleHandleW(null);
        _hookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL,
            Marshal.GetFunctionPointerForDelegate(_hookProcDelegate),
            _moduleHandle,
            0);

        if (_hookHandle == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"SetWindowsHookEx(WH_MOUSE_LL) 失败: {error}");
        }

        _logger.LogInformation("WH_MOUSE_LL Hook 已安装 (handle=0x{Handle:X})", _hookHandle);

        // HoldDetect 后台循环
        _ = Task.Run(HoldDetectLoopAsync, _cts.Token);

        await Task.CompletedTask;
    }

    public async Task UninstallAsync(CancellationToken ct)
    {
        if (_hookHandle == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = nint.Zero;
        _logger.LogInformation("WH_MOUSE_LL Hook 已卸载");
        await Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_hookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }
        _cts.Cancel();
        _cts.Dispose();
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                ProcessMouseMessage(wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hook 回调异常");
        }
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void ProcessMouseMessage(nint wParam, nint lParam)
    {
        var msg = (int)wParam;
        var raw = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

        _events.EmitRawInputRaw(raw.pt.X, raw.pt.Y, raw.time);

        switch (msg)
        {
            case NativeMethods.WM_XBUTTONDOWN when (raw.mouseData >> 16) == 1:
                OnButtonDown(NativeMethods.VK_XBUTTON1, raw.pt, raw.time);
                break;
            case NativeMethods.WM_XBUTTONDOWN when (raw.mouseData >> 16) == 2:
                OnButtonDown(NativeMethods.VK_XBUTTON2, raw.pt, raw.time);
                break;
            case NativeMethods.WM_MBUTTONDOWN:
                OnButtonDown(NativeMethods.VK_MBUTTON, raw.pt, raw.time);
                break;
            case NativeMethods.WM_RBUTTONDOWN:
                OnButtonDown(NativeMethods.VK_RBUTTON, raw.pt, raw.time);
                break;

            case NativeMethods.WM_XBUTTONUP when _pressedButtonVk is NativeMethods.VK_XBUTTON1 or NativeMethods.VK_XBUTTON2:
            case NativeMethods.WM_MBUTTONUP when _pressedButtonVk == NativeMethods.VK_MBUTTON:
            case NativeMethods.WM_RBUTTONUP when _pressedButtonVk == NativeMethods.VK_RBUTTON:
                OnButtonUp();
                break;
        }
    }

    private void OnButtonDown(int vk, NativeMethods.POINT pt, uint rawTime)
    {
        if (_pressedButtonVk != 0)
        {
            return;
        }
        _pressedButtonVk = vk;
        _pressPoint = pt;
        _pressTimestampMs = Environment.TickCount64;
        _holdFired = false;
    }

    private void OnButtonUp()
    {
        if (_pressedButtonVk == 0)
        {
            return;
        }

        if (!_holdFired)
        {
            // 按下后立即松开，不视为长按触发
        }

        _pressedButtonVk = 0;
        _lastReleaseAt = DateTimeOffset.UtcNow;
    }

    private async Task HoldDetectLoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            if (_pressedButtonVk != 0 && !_holdFired)
            {
                var elapsed = Environment.TickCount64 - _pressTimestampMs;
                if (elapsed >= HoldThresholdMs)
                {
                    var triggerType = MapButtonToTrigger(_pressedButtonVk);
                    if (triggerType != TriggerType.None)
                    {
                        var origin = new RingPoint(_pressPoint.X, _pressPoint.Y);
                        var evt = new SpatialIntentEvent(
                            triggerType,
                            origin,
                            1.0f,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            ModifierState.None);
                        _events.EmitIntent(evt);
                        _holdFired = true;
                    }
                }
            }
            try
            {
                await Task.Delay(16, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private static TriggerType MapButtonToTrigger(int vk) => vk switch
    {
        NativeMethods.VK_XBUTTON1 => TriggerType.MouseSideButton,
        NativeMethods.VK_XBUTTON2 => TriggerType.MouseSideButton,
        NativeMethods.VK_MBUTTON => TriggerType.MiddleButton,
        NativeMethods.VK_RBUTTON => TriggerType.RightButtonLongPress,
        _ => TriggerType.None,
    };
}

/// <summary>
/// 事件转发的内部容器。EventHandler 在构造时生成 delegate 链。
/// </summary>
internal sealed class InputAdapterEvents
{
    public event EventHandler<SpatialIntentEvent>? IntentEmitted;
    public event EventHandler<RawInputEvent>? RawInputEmitted;

    public void EmitIntent(SpatialIntentEvent evt) => IntentEmitted?.Invoke(this, evt);
    public void EmitRawInputRaw(int x, int y, uint rawTime)
    {
        var ms = rawTime == 0 ? Environment.TickCount64 : rawTime;
        RawInputEmitted?.Invoke(this, new RawInputEvent(x, y, ms));
    }
}