using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SecRandom.Core.Services.Camera;
using System.Reflection;

namespace SecRandom.Core.Tests;

public sealed class DamoYoloModelDiagnosticsTests
{
    [Fact]
    public void DamoYoloHeadModel_UsesExpectedEndToEndContract()
    {
        var path = Path.Combine(FindRepositoryRoot(), "data", "cv_models", "damoyolo_end2end_simplified.onnx");
        using var session = new InferenceSession(path);

        Assert.Equal(new[] { 1, 3, 640, 640 }, session.InputMetadata["images"].Dimensions);
        Assert.Contains("num_dets", session.OutputMetadata.Keys);
        Assert.Contains("det_boxes", session.OutputMetadata.Keys);
        Assert.Contains("det_scores", session.OutputMetadata.Keys);
        Assert.Contains("det_classes", session.OutputMetadata.Keys);
    }

    [Fact]
    public void DamoYoloPreprocessing_PreservesRawRgbPixelRange()
    {
        var method = typeof(CameraDrawEngine).GetMethod(
            "CreateDamoyoloTensorFromMat",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        using var frame = new Mat(1, 1, MatType.CV_8UC3, new Scalar(20, 40, 80));

        var result = method.Invoke(null, [frame])!;
        var tensor = (DenseTensor<float>)result.GetType().GetProperty("Tensor")!.GetValue(result)!;

        Assert.Equal(80.0f, tensor[0, 0, 0, 0]);
        Assert.Equal(40.0f, tensor[0, 1, 0, 0]);
        Assert.Equal(20.0f, tensor[0, 2, 0, 0]);
    }

    [Fact]
    public void DamoYoloHeadModel_ProducesEndToEndOutputsForBlackFrame()
    {
        var path = Path.Combine(FindRepositoryRoot(), "data", "cv_models", "damoyolo_end2end_simplified.onnx");
        using var session = new InferenceSession(path);
        var input = new DenseTensor<float>(new[] { 1, 3, 640, 640 });
        using var results = session.Run([NamedOnnxValue.CreateFromTensor("images", input)]);

        var numDets = results.Single(result => result.Name == "num_dets").AsTensor<long>();
        var boxes = results.Single(result => result.Name == "det_boxes").AsTensor<float>();
        var scores = results.Single(result => result.Name == "det_scores").AsTensor<float>();
        var classes = results.Single(result => result.Name == "det_classes").AsTensor<long>();

        Assert.Single(numDets.ToArray());
        Assert.Equal(4, boxes.Dimensions[^1]);
        Assert.Equal(boxes.Dimensions[^2], scores.Dimensions[^1]);
        Assert.Equal(scores.Dimensions[^1], classes.Dimensions[^1]);
    }

    [Fact]
    public void Rfb640FaceModel_ReportsInputAndOutputContract()
    {
        var path = Path.Combine(FindRepositoryRoot(), "data", "cv_models", "version-RFB-640.onnx");
        using var session = new InferenceSession(path);

        var input = new DenseTensor<float>(new[] { 1, 3, 480, 640 });
        using var results = session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        var scores = results.Single(result => result.Name == "scores").AsTensor<float>().ToArray();
        var boxes = results.Single(result => result.Name == "boxes").AsTensor<float>().ToArray();

        throw new Xunit.Sdk.XunitException(
            $"RFB-640 scores=[{scores.Min():0.000},{scores.Max():0.000}], boxes=[{boxes.Min():0.000},{boxes.Max():0.000}], " +
            $"first_box=[{string.Join(",", boxes.Take(4).Select(value => value.ToString("0.000"))}]");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecRandom.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate SecRandom.sln.");
    }
}
