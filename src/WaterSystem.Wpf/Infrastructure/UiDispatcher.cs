using System.Windows;
using System.Windows.Threading;

namespace WaterSystem.Wpf.Infrastructure
{
    /// <summary>
    /// UI 封送唯一入口（决策 D9，对应源 InvokeRequired/Invoke 模式，FrmMain.cs:207-211）。
    /// 纪律：**只允许 Post（异步派发），不提供同步 Invoke**——规避"工作线程同步等 UI、
    /// UI 又在等工作线程"的死锁形态（迁移方案风险 R4）；代码评审须检查所有封送点。
    /// Runner 可注入以支持无 WPF Application 的单元测试（T2.4）。
    /// </summary>
    public static class UiDispatcher
    {
        public static Action<Action> Runner { get; set; } =
            action => Application.Current?.Dispatcher.InvokeAsync(action, DispatcherPriority.Normal);

        /// <summary>把动作派发到 UI 线程（服务事件 → VM 的唯一封送路径）。</summary>
        public static void Post(Action action) => Runner(action);
    }
}
