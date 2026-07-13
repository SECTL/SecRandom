namespace SecRandom.Core.Enums.Configs;

public enum FaceDetectorMode
{
    Lightweight,
    Enhanced
}

public static class FaceDetectorModeResolver
{
    public const string YuNetModelFileName = "face_detection_yunet_2023mar_int8bq.onnx";
    public const string DamoyoloModelFileName = "damoyolo_end2end_simplified.onnx";

    public static FaceDetectorMode ResolveMode(string? modelFileName)
    {
        return string.Equals(
                   Path.GetFileName(modelFileName),
                   DamoyoloModelFileName,
                   StringComparison.OrdinalIgnoreCase)
            ? FaceDetectorMode.Enhanced
            : FaceDetectorMode.Lightweight;
    }

    public static string GetModelFileName(FaceDetectorMode mode)
    {
        return mode == FaceDetectorMode.Enhanced
            ? DamoyoloModelFileName
            : YuNetModelFileName;
    }

    public static TimeSpan GetInferenceInterval(FaceDetectorMode mode)
    {
        return mode == FaceDetectorMode.Enhanced
            ? TimeSpan.FromMilliseconds(250)
            : TimeSpan.FromMilliseconds(100);
    }
}
