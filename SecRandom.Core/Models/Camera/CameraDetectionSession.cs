namespace SecRandom.Core.Models.Camera;

public record CameraDetectionSession(
    Guid SessionId,
    DateTimeOffset StartedAt
    );
