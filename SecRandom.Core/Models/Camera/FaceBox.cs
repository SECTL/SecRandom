namespace SecRandom.Core.Models.Camera;

//约定：左上角为(0,0)
public sealed record FaceBox(
    float X1,
    float Y1,
    float X2,
    float Y2,
    float? Confidence = null);