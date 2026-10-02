namespace FlowRing.DesktopBridge.Abstractions;

/// <summary>
/// 抛出时机：Host 调用 Bridge 但 InitializeAsync 尚未完成。
/// 用于提示 Phase 3/4 单元测试区分"未初始化"与"真实失败"。
/// </summary>
public sealed class BridgeNotInitializedException : InvalidOperationException
{
    public BridgeNotInitializedException()
        : base("DesktopBridge.InitializeAsync() 未完成或失败。请先 InitializeAsync 再使用其他 API。")
    {
    }
}