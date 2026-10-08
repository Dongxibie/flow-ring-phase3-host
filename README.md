> **本仓库是 [Flow Ring](https://github.com/Dongxibie/flow-ring-phase7-package) 的分阶段开发记录（第 3 步 · 桌面宿主与 Win32 桥接）。**
> 完整产品（源码 / 截图 / 下载即用的 Windows 安装包）在 **[flow-ring-phase7-package](https://github.com/Dongxibie/flow-ring-phase7-package)**，建议从产品仓库开始了解本项目。

---

# Flow Ring · 第 3 步：桌面宿主与 Win32 桥接

Flow Ring 是一个 Windows 桌面快捷环：按住鼠标侧键唤出悬浮圆环，把光标拖向某个方向后松开，即可执行常用动作，不用离开当前窗口。项目按开发阶段拆分为 7 个仓库，本仓库是其中的第 3 步。

## 本步骤完成内容

DesktopBridge 与 Host 的真实实现：

- Win32 P/Invoke 完整集合
- `WH_MOUSE_LL` 钩子的侧键长按识别
- `SendInput` 注入
- `GetForegroundWindow` 上下文检测
- WebView2Environment 真实初始化
- Named Pipe 服务端
- WinForms 系统托盘（运行 = 绿 / 暂停 = 灰 状态图 + 完整菜单）

## 验证结果（阶段记录）

- `dotnet build` 0 警告 0 错误
