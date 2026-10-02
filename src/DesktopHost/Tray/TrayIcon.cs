using System.Drawing;
using System.Drawing.Drawing2D;
using FlowRing.DesktopHost.Host;

namespace FlowRing.DesktopHost.Tray;

/// <summary>
/// 系统托盘完整版。状态变化显示 Running（绿点）+ Paused（灰点）+ Idle（黄点）。
/// 菜单项 click 调用 HostController 路由到 WebView2 加载对应页面或调用 host API。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly HostController _controller;
    private NotifyIcon? _notifyIcon;

    public TrayIcon(HostController controller)
    {
        _controller = controller;
    }

    public void Initialize()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Studio", null, (_, _) => _controller.NavigateTo("studio"));
        menu.Items.Add("打开 Profile Manager", null, (_, _) => _controller.NavigateTo("profiles"));
        menu.Items.Add(new ToolStripSeparator());
        var paused = new ToolStripMenuItem("暂停 Flow Ring")
        {
            CheckOnClick = true,
            Checked = _controller.IsPaused,
        };
        paused.CheckedChanged += (_, _) =>
        {
            _controller.SetPaused(paused.Checked);
            UpdateIcon();
        };
        menu.Items.Add(paused);
        menu.Items.Add("设置", null, (_, _) => _controller.NavigateTo("settings"));
        menu.Items.Add("关于", null, (_, _) => _controller.NavigateTo("about"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _controller.ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = BuildIcon(_controller.IsPaused, _controller.IsActive),
            Visible = true,
            Text = _controller.IsPaused ? "Flow Ring（暂停）" : "Flow Ring（运行）",
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _controller.NavigateTo("studio");
    }

    public void UpdateIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Icon = BuildIcon(_controller.IsPaused, _controller.IsActive);
        _notifyIcon.Text = _controller.IsPaused ? "Flow Ring（暂停）" : "Flow Ring（运行）";
    }

    public void ShowNotification(string text, string title = "Flow Ring")
    {
        _notifyIcon?.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
    }

    /// <summary>
    /// 状态图：16x16 圆点。
    /// 运行中 = 绿色；暂停 = 灰色；异常 = 黄色（占位）。
    /// </summary>
    private static Icon BuildIcon(bool paused, bool active)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var color = paused ? Color.Gray : (active ? Color.FromArgb(255, 0, 180, 0) : Color.FromArgb(255, 220, 200, 0));
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);
            using var pen = new Pen(Color.Black, 1f);
            g.DrawEllipse(pen, 1, 1, 14, 14);
        }
        var hicon = bmp.GetHicon();
        return Icon.FromHandle(hicon);
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}