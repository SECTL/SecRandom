using System.Text.Json;
using SecRandom.Core.Services.Ipc;
using SecRandom.Services.Ipc;
using SecRandom.Shared.Models.Ipc;

namespace SecRandom.Core.Tests;

public class AutomationIpcTests
{
    [Fact]
    public void V2Request_ParsesDocumentedEnvelope()
    {
        const string line = """
                           {"version":2,"type":"rpc","id":"7","method":"system.ping","params":{}}
                           """;

        var ok = IpcV2RequestEnvelope.TryRead(line, out var request, out var error);

        Assert.True(ok, error?.Message);
        Assert.NotNull(request);
        Assert.Equal("7", request!.Id);
        Assert.Equal("system.ping", request.Method);
    }

    [Fact]
    public void V2Request_RejectsV1AndUnknownTypes()
    {
        Assert.False(IpcV2RequestEnvelope.TryRead(
            """{"version":1,"type":"url","payload":{"url":"data/roll_call_list?name=a"}}""",
            out _, out var versionError));
        Assert.Equal(AutomationErrorCodes.UnsupportedVersion, versionError!.Code);

        Assert.False(IpcV2RequestEnvelope.TryRead(
            """{"version":2,"type":"event","event":"x"}""",
            out _, out var typeError));
        Assert.Equal(AutomationErrorCodes.InvalidRequest, typeError!.Code);
    }

    [Fact]
    public void V2Response_SerializesOkAndErrorBranches()
    {
        var success = JsonSerializer.Serialize(
            IpcV2ResponseEnvelope.Success("1", new { pong = true }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"ok\":true", success);
        Assert.Contains("\"pong\":true", success);

        var failure = JsonSerializer.Serialize(
            IpcV2ResponseEnvelope.Failure("2", IpcV2ErrorEnvelope.Create("busy", "忙", true)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"ok\":false", failure);
        Assert.Contains("\"retryable\":true", failure);
    }

    [Fact]
    public void CommandCatalog_IsUniqueAndStable()
    {
        var methods = AutomationCommandCatalog.Methods;
        Assert.Equal(37, methods.Count);
        Assert.Equal(methods.Count, methods.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(methods, m => Assert.False(string.IsNullOrWhiteSpace(m.Name)));
        Assert.NotNull(AutomationCommandCatalog.Find("draw.quick.trigger"));
        Assert.Null(AutomationCommandCatalog.Find("nope"));
    }

    [Theory]
    [InlineData("roster.read", "{roster_kind?,list_name?,include_disabled?}", "{roster_kind,lists}")]
    [InlineData("draw.await", "{target?,timeout_ms?}", "{target,students|prizes|student}")]
    [InlineData("history.summary", "{kind?,name}", "{kind,name,rounds,count}")]
    public void CommandCatalog_DescribesWireFields(string method, string parameters, string result)
    {
        var descriptor = AutomationCommandCatalog.Find(method);
        Assert.NotNull(descriptor);
        Assert.Equal(parameters, descriptor.Params);
        Assert.Equal(result, descriptor.Result);
    }

    [Fact]
    public void DrawRequest_AcceptsSparseOverridesWithoutInventingNullFields()
    {
        var parameters = JsonSerializer.SerializeToElement(new { list = "A", count = 2 });
        var request = AutomationCommandRegistry.ParseDrawRequest("roll_call", parameters);
        Assert.Equal("A", request.ListName);
        Assert.Equal(2, request.Count);
        Assert.Null(request.Gender);
        Assert.Null(request.Conditions);
    }

    [Fact]
    public void DrawRequest_RejectsInvalidAndInapplicableOverrides()
    {
        var invalid = JsonSerializer.Deserialize<JsonElement>("""{"count":null}""");
        var lotteryGender = JsonSerializer.SerializeToElement(new { gender = "F" });
        Assert.Equal(AutomationErrorCodes.InvalidParameter,
            Assert.Throws<AutomationRpcException>(() => AutomationCommandRegistry.ParseDrawRequest("roll_call", invalid)).Code);
        Assert.Equal(AutomationErrorCodes.InvalidParameter,
            Assert.Throws<AutomationRpcException>(() => AutomationCommandRegistry.ParseDrawRequest("lottery", lotteryGender)).Code);
    }

    [Fact]
    public void DrawRequest_PreservesLotteryConditions()
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>("""{"conditions":{"prize_tags":["gold"],"student_list":"A","group":"1"}}""");
        var request = AutomationCommandRegistry.ParseDrawRequest("lottery", parameters);
        Assert.NotNull(request.Conditions);
        Assert.Equal("A", request.Conditions.StudentList);
        Assert.Equal("1", request.Conditions.Group);
        Assert.Equal(["gold"], request.Conditions.PrizeTags);
    }

    [Fact]
    public void ErrorCodes_AreLowerCaseSnakeCase()
    {
        var codes = typeof(AutomationErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Contains(AutomationErrorCodes.IpcDisabled, codes);
        Assert.Contains(AutomationErrorCodes.UrlActivationForbidden, codes);
        Assert.All(codes, c => Assert.Matches("^[a-z][a-z0-9_]*$", c));
    }
}
