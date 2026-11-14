using System.Text.Json;
using SecRandom.Core.Services.Ipc;
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