using System.Drawing;
using System.Reflection;
using System.Resources;

namespace FlowRing.DesktopHost.Tray;

/// <summary>
/// 系统托盘占位实现。MVP 仅显示托盘图标 + 右键菜单骨架；
/// Phase 3 补充：启用/暂停状态图（绿点/灰点）、菜单项的 click handler、图标嵌入资源。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private NotifyIcon? _notifyIcon;

    public void Initialize()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Studio", null, (_, _) => OpenStudio());
        menu.Items.Add("打开 Profile Manager", null, (_, _) => OpenProfileManager());
        menu.Items.Add(new ToolStripSeparator());
        var paused = new ToolStripMenuItem("暂停 Flow Ring")
        {
            CheckOnClick = true,
        };
        paused.CheckedChanged += (_, _) => OnPausedChange(paused.Checked);
        menu.Items.Add(paused);
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关于", null, (_, _) => OpenAbout());
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Flow Ring",
            ContextMenuStrip = menu,
        };
    }

    private static void OpenStudio() { }
    private static void OpenProfileManager() { }
    private static void OpenSettings() { }
    private static void OpenAbout() { }
    private static void OnPausedChange(bool isPaused) { }
    private static void ExitApp() => Application.Exit();

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}