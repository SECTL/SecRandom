namespace SecRandom.Core.Models.Camera;

public enum DetectionState
{
    NotRun,
    NoFace,
    HasFaces,
    Error
}

public sealed record CameraFramePacket(
    Guid SessionId,
    long FrameId,
    int Width,
    int Height,
    IReadOnlyList<FaceBox> Faces,
    DetectionState State,
    byte[] BgraBuffer
);