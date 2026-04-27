namespace SecRandom.Core.Interfaces;

public interface IRandomSource
{
    /// <summary>
    /// 返回一个不大于 maxExclusive 的 int32 数字
    /// </summary>
    int NextInt32(int maxExclusive);
    
    /// <summary>
    /// 返回一个 [0,1.0) 的 double
    /// </summary>
    double NextDouble();
}
