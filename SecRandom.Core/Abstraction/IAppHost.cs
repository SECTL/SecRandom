using Microsoft.Extensions.Hosting;

namespace SecRandom.Core.Abstraction;

/// <summary>
///     应用主机接口
/// </summary>
public interface IAppHost
{
    /// <summary>
    ///     应用主机
    /// </summary>
    public static IHost? Host;

    /// <summary>
    ///     获取指定的服务
    /// </summary>
    /// <typeparam name="T">要获取的服务类型</typeparam>
    /// <returns>获取的服务</returns>
    /// <exception cref="ArgumentException"></exception>
    public static T GetService<T>()
    {
        var s = Host?.Services.GetService(typeof(T));
        if (s != null) return (T)s;

        throw new ArgumentException($"Service {typeof(T)} is null!");
    }

    /// <summary>
    ///     尝试获取指定的服务
    /// </summary>
    /// <typeparam name="T">要获取的服务类型</typeparam>
    /// <returns>如果获取成功，则返回获取到的服务，否则返回null</returns>
    public static T? TryGetService<T>()
    {
        try
        {
            return (T?)Host?.Services.GetService(typeof(T));
        }
        catch (ObjectDisposedException)
        {
            // 停机流程会先释放服务容器、再清空 Host：这段窗口里解析服务会抛 ObjectDisposedException。
            // 崩溃处理路径本身也要取服务保存配置，因此这里必须按“服务不可用”处理，
            // 否则一次停机期的取服务会把异常处理再次打崩。
            return default;
        }
    }
}