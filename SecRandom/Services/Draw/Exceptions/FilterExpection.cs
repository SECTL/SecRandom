using System;

namespace SecRandom.Services.Draw.Exceptions;

public class CandidateNotFoundException : Exception
{
    public CandidateNotFoundException() { }
    public CandidateNotFoundException(string message):base(message) { }
    public CandidateNotFoundException(string message, Exception inner)
        : base(message, inner) { }
}
