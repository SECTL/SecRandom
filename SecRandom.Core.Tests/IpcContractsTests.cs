using System.Text.Json;
using SecRandom.Shared.Models.Ipc;

namespace SecRandom.Core.Tests;

public class IpcContractsTests
{
    [Fact]
    public void RequestEnvelope_UsesDocumentedJsonNames()
    {
        var request = new IpcRequestEnvelope(1, "url", new IpcRequestPayload("data/roll_call_list?name=一班"));

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"type\":\"url\"", json);
        Assert.Contains("\"url\":\"data/roll_call_list?name=", json);
    }

    [Fact]
    public void BusinessFailure_RemainsAValidProtocolResponse()
    {
        var response = new IpcResponseEnvelope(
            true,
            "url",
            new IpcBusinessResult("error", "未找到名单。", "not_found"));

        Assert.True(response.Success);
        Assert.Equal("error", response.Result!.Status);
        Assert.Equal("not_found", response.Result.Code);
        Assert.Null(response.Error);
    }
}
