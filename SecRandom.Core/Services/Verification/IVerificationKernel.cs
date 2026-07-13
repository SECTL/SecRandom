using SecRandom.Core.Models.Verification;

namespace SecRandom.Core.Services.Verification;

public interface IVerificationKernel
{
    VerificationKernelResult Draw(VerificationDrawInput input, ReadOnlySpan<byte> seed);
}
