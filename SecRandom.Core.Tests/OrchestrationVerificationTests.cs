using System.Text.Json;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Orchestration;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Profile;
using Xunit;

namespace SecRandom.Core.Tests;

public sealed class OrchestrationVerificationTests
{
    [Fact]
    public void CreatePrizeInput_AssignsOccurrenceIndexesToRepeatedInventoryUnits()
    {
        var recordId = Guid.NewGuid();
        var prize = new Prize { RecordId = recordId, Name = "Two tickets", Count = 2 };

        var input = OrchestrationVerification.CreatePrizeInput(
            [prize, prize],
            round: 1,
            roundCount: 1,
            countMode: true);

        Assert.Equal(2, input.Count);
        using var audit = JsonDocument.Parse(input.AuditPayload);
        Assert.Equal("count", audit.RootElement.GetProperty("fairness").GetProperty("lotteryMode").GetString());
        Assert.Equal(
            [0u, 1u],
            input.Candidates.Select(candidate => candidate.OccurrenceIndex));
        Assert.Equal(
            [1L, 2L],
            input.Candidates.Select(candidate => candidate.WeightMicros));

        var result = new ManagedVerificationKernel().Draw(input, new byte[32]);

        Assert.Equal(
            [(recordId, 0u), (recordId, 1u)],
            result.Winners.Select(winner => (winner.RecordId, winner.OccurrenceIndex)));
    }

    [Fact]
    public void CreatePrizeInput_RestartsOccurrenceIndexesForTheCurrentRemainingPool()
    {
        var recordId = Guid.NewGuid();
        var prize = new Prize { RecordId = recordId, Name = "One remaining ticket", Count = 2 };

        var input = OrchestrationVerification.CreatePrizeInput(
            [prize],
            round: 1,
            roundCount: 1,
            countMode: true);

        Assert.Equal(0u, Assert.Single(input.Candidates).OccurrenceIndex);
    }
}
