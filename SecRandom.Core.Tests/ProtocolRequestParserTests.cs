using SecRandom.Core.Services.Ipc;

namespace SecRandom.Core.Tests;

public class ProtocolRequestParserTests
{
    [Fact]
    public void TryParse_UsesLastActionAliasAndNormalizesRoute()
    {
        var parsed = ProtocolRequestParser.TryParse(
            "secrandom://WINDOW/Main?mode=hide&visible=1",
            requireSecRandomScheme: true,
            out var request,
            out var failure);

        Assert.True(parsed);
        Assert.Null(failure);
        Assert.Equal("window/main", request!.Route);
        Assert.Equal("1", ProtocolRequestParser.GetLast(request.Query, "action", "mode", "op", "do", "visible"));
    }

    [Fact]
    public void TryParse_AcceptsRelativeIpcRouteAndPreservesDecodedName()
    {
        var parsed = ProtocolRequestParser.TryParse(
            "data/roll_call_list?name=%E4%B8%80%E7%8F%AD",
            requireSecRandomScheme: false,
            out var request,
            out var failure);

        Assert.True(parsed);
        Assert.Null(failure);
        Assert.False(request!.IsFullUri);
        Assert.Equal("一班", ProtocolRequestParser.GetLast(request.Query, "name"));
    }

    [Theory]
    [InlineData("secrandom://window/main?name=%")]
    [InlineData("secrandom://window/main?name=%ZZ")]
    [InlineData("secrandom://window/main?name=%0A")]
    public void TryParse_RejectsMalformedOrControlQueryValues(string value)
    {
        var parsed = ProtocolRequestParser.TryParse(value, true, out _, out var failure);

        Assert.False(parsed);
        Assert.NotNull(failure);
    }

    [Fact]
    public void TryParse_RejectsWrongSchemeForUrlActivation()
    {
        var parsed = ProtocolRequestParser.TryParse("https://example.test/window/main", true, out _, out var failure);

        Assert.False(parsed);
        Assert.Equal("invalid_command", failure!.Code);
    }

    [Theory]
    [InlineData("secrandom://window/main#fragment")]
    [InlineData("secrandom://user@window/main")]
    [InlineData("secrandom://window:123/main")]
    [InlineData("secrandom://window/main?=value")]
    public void TryParse_RejectsAmbiguousUrlForms(string value)
    {
        var parsed = ProtocolRequestParser.TryParse(value, true, out _, out var failure);

        Assert.False(parsed);
        Assert.NotNull(failure);
    }

    [Fact]
    public void TryParse_RejectsRequestsOverLimit()
    {
        var request = "secrandom://window/main?value=" + new string('a', ProtocolRequestParser.MaxRequestLength);

        var parsed = ProtocolRequestParser.TryParse(request, true, out _, out var failure);

        Assert.False(parsed);
        Assert.Equal("invalid_request", failure!.Code);
    }
}
