namespace SecRandom.Services.Draw;

public interface IRandomSource
{
    int NextInt32(int maxExclusive); // 返回一个不大于maxExclusive的int32
    double NextDouble(); //返回一个[0,1.0)的double
}
