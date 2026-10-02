using FlowRing.DesktopBridge.Abstractions;

namespace FlowRing.DesktopBridge.Win32;

public sealed class Win32SystemInfo : ISystemInfo
{
    public string Platform => "win32";

    public string OSVersion
    {
        get
        {
            var ver = new NativeMethods.OSVERSIONINFOEXW
            {
                dwOSVersionInfoSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.OSVERSIONINFOEXW>(),
            };
            if (NativeMethods.GetVersionExW(ref ver))
            {
                return $"Windows {ver.dwMajorVersion}.{ver.dwMinorVersion}.{ver.dwBuildNumber}";
            }
            return "Windows";
        }
    }

    public string AppVersion => AssemblyVersion.GetEntry();

    public string UserDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlowRing");
}

/// <summary>
/// MVP 占位的 AssemblyVersion 读取器；Phase 6 起接入真实构建号。
/// </summary>
internal static class AssemblyVersion
{
    private static readonly string _cached = Compute();

    public static string GetEntry() => _cached;

    private static string Compute()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            return asm?.GetName().Version?.ToString() ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }
}