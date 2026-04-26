using System;

namespace SecRandom.Services.Draw.Exceptions;

public class CandidateNotFoundException : Exception
{
    public CandidateNotFoundException() { }
    public CandidateNotFoundException(string message):base(message) { }
    public CandidateNotFoundException(string message, Exception inner)
        : base(message, inner) { }
}

public class RepeatLimitExhaustedException : Exception
{
    public RepeatLimitExhaustedException() { }
    public RepeatLimitExhaustedException(string message) : base(message) { }
    public RepeatLimitExhaustedException(string message, Exception inner)
        : base(message, inner) { }
}
