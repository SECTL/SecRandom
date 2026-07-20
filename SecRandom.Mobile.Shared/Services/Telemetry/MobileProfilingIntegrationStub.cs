// ReSharper disable once CheckNamespace
namespace Sentry;

/// <summary>
/// 移动端 no-op 占位：Sentry.Profiling 包不支持 Android/iOS（其 buildTransitive targets
/// 在这两个平台上直接报 MSBuild 错误），因此移动共享库不能引用它。链接编译的
/// SentryTelemetrySdkAdapter 仍以 using Sentry 解析 AddProfilingIntegration 扩展方法，
/// 由本类型满足；适配器内部的 OperatingSystem 门保证移动平台永远不会真正执行该调用。
/// 桌面 SecRandom 程序集继续通过 Sentry.Profiling 包获得真实实现，互不影响。
/// </summary>
internal static class MobileProfilingIntegrationStub
{
    public static void AddProfilingIntegration(this SentryOptions options, TimeSpan startupTimeout)
    {
        _ = options;
        _ = startupTimeout;
    }
}
