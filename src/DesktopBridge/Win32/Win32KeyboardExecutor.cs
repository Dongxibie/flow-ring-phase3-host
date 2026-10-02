using System.Runtime.InteropServices;
using FlowRing.RingCore.Action;
using Microsoft.Extensions.Logging;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// SendInput 注入键盘按键。
/// payload.op 与 src/SharedSchema/schemas/action.schema.json 的 keyboardPayload 对齐：
/// { "keys": "Ctrl+Shift+T", "modifiers": 3 }
/// MVP 占位实现：actionId 形如 "key-ctrl-shift-t"，modifier 仅识别 "Ctrl / Shift / Alt / Win"。
/// </summary>
public sealed class Win32KeyboardExecutor : IActionExecutor
{
    private readonly ILogger<Win32KeyboardExecutor> _logger;

    public Win32KeyboardExecutor(ILogger<Win32KeyboardExecutor> logger)
    {
        _logger = logger;
    }

    public ActionKind Kind => ActionKind.Keyboard;

    public PermissionTier RequiredTier => PermissionTier.Normal;

    public ValueTask<ExecutionResult> ExecuteAsync(string actionId, ActionContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var parts = actionId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !parts[0].Equals("key", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return ValueTask.FromResult(new ExecutionResult(false, $"键盘 Action 格式错误：{actionId}", sw.ElapsedMilliseconds));
            }
            var keyTokens = parts.Skip(1).ToArray();
            var (success, error) = SimulateHotkey(keyTokens);
            sw.Stop();
            return ValueTask.FromResult(new ExecutionResult(success, error, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ValueTask.FromResult(new ExecutionResult(false, ex.Message, sw.ElapsedMilliseconds));
        }
    }

    private static (bool Success, string? Error) SimulateHotkey(string[] tokens)
    {
        var modifiers = 0u;
        ushort vk = 0;
        foreach (var token in tokens)
        {
            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= 0x0002; break;
                case "SHIFT":
                    modifiers |= 0x0001; break;
                case "ALT":
                    modifiers |= 0x0004; break;
                case "WIN":
                    modifiers |= 0x0008; break;
                default:
                    vk = CharToVk(token); break;
            }
        }
        if (vk == 0)
        {
            return (false, "未识别的主键");
        }
        // 按下 modifier（Win=0x5B / Ctrl=0x11 / Shift=0x10 / Alt=0x12）
        if ((modifiers & 0x0008) != 0 && !SendKeyEvent((ushort)0x5B, false)) return (false, "Win down 失败");
        if ((modifiers & 0x0001) != 0 && !SendKeyEvent((ushort)0x10, false)) return (false, "Shift down 失败");
        if ((modifiers & 0x0002) != 0 && !SendKeyEvent((ushort)0x11, false)) return (false, "Ctrl down 失败");
        if ((modifiers & 0x0004) != 0 && !SendKeyEvent((ushort)0x12, false)) return (false, "Alt down 失败");
        if (!SendKeyEvent(vk, false)) return (false, "主键 down 失败");
        if (!SendKeyEvent(vk, true)) return (false, "主键 up 失败");
        if ((modifiers & 0x0004) != 0) SendKeyEvent((ushort)0x12, true);
        if ((modifiers & 0x0002) != 0) SendKeyEvent((ushort)0x11, true);
        if ((modifiers & 0x0001) != 0) SendKeyEvent((ushort)0x10, true);
        if ((modifiers & 0x0008) != 0) SendKeyEvent((ushort)0x5B, true);
        return (true, null);
    }

    private static bool SendKeyEvent(ushort vk, bool up)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0u,
                },
            },
        };
        var size = Marshal.SizeOf<NativeMethods.INPUT>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(input, ptr, false);
            var ret = NativeMethods.SendInput(1, ptr, size);
            return ret == 1;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static ushort CharToVk(string token)
    {
        if (token.Length == 1)
        {
            var c = token[0];
            var scan = NativeMethods.VkKeyScanW(c);
            return (ushort)(scan & 0xFF);
        }
        return token.ToUpperInvariant() switch
        {
            "ESC" => 0x1B,
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
            _ => 0,
        };
    }
}