using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Models.Camera;
using SecRandom.Core.Services.Camera;
using SecRandom.Core.Services.Config;

namespace SecRandom.Core.Tests;

public class CameraPreviewDetectionTests
{
    [Fact]
    public void DetectFacesForPreview_PreservesLatestValidDetectionAfterInferenceFailure()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.None));
                services.AddSingleton<ConfigServiceBase, TestConfigService>();
                services.AddSingleton<MainConfigHandler>();
            })
            .Build();
        IAppHost.Host = host;

        try
        {
            var engine = new CameraDrawEngine();
            var expectedFaces = new[] { new FaceBox(1, 2, 3, 4, 0.9f) };
            SetField(engine, "_lastDetectedFaces", expectedFaces);
            SetField(engine, "_lastDetectionState", DetectionState.HasFaces);

            using var frame = new Mat();
            var result = ((ValueTuple<IReadOnlyList<FaceBox>, DetectionState>)typeof(CameraDrawEngine)
                .GetMethod("DetectFacesForPreview", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(engine, [frame])!)!;

            Assert.Equal(expectedFaces, result.Item1);
            Assert.Equal(DetectionState.HasFaces, result.Item2);
        }
        finally
        {
            IAppHost.Host = null;
        }
    }

    private static void SetField<T>(CameraDrawEngine engine, string name, T value)
    {
        typeof(CameraDrawEngine)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(engine, value);
    }

    private sealed class TestConfigService : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => false;

        public override T LoadConfig<T>(T fallback) => fallback;

        public override void SaveConfig<T>(T config)
        {
        }

        public override void DeleteConfig<T>(T config)
        {
        }
    }
}
