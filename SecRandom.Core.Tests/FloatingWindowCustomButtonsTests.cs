using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Platforms;

namespace SecRandom.Core.Tests;

public class FloatingWindowCustomButtonsTests
{
    [Fact]
    public void CustomButtons_RoundTripThroughConfigJson()
    {
        MainConfigModel config = new();
        config.FloatingWindowSettings.CustomButtons.Add(new FloatingWindowButtonConfig
        {
            Enabled = false,
            Label = "教室",
            IconGlyph = "\uE716",
            ActionKind = FloatingWindowButtonActionKind.Uri,
            ActionTarget = "https://example.org/"
        });

        string json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"custom_buttons\"", json, StringComparison.Ordinal);
        Assert.NotNull(restored);
        var button = Assert.Single(restored.FloatingWindowSettings.CustomButtons);
        Assert.False(button.Enabled);
        Assert.Equal("教室", button.Label);
        Assert.Equal(FloatingWindowButtonActionKind.Uri, button.ActionKind);
        Assert.Equal("https://example.org/", button.ActionTarget);
    }

    [Fact]
    public void GetRenderableButtons_KeepsOrderAndSkipsInvisibleEntries()
    {
        var first = CreateButton("点名", icon: "A");
        var disabled = CreateButton("隐藏", icon: "B", enabled: false);
        var blankLabel = CreateButton(" ", icon: "C");
        var noIcon = CreateButton("无图标");
        var second = CreateButton("网址", icon: "D", kind: FloatingWindowButtonActionKind.Uri,
            target: "https://example.org/");

        var renderable = FloatingWindowButtonCatalog.GetRenderableButtons(
            [first, disabled, blankLabel, noIcon, second]);

        Assert.Equal([first, second], renderable);
    }

    [Fact]
    public void GetRenderableButtons_CapsAtMaxWithoutDeletingConfiguredEntries()
    {
        var buttons = Enumerable.Range(0, FloatingWindowButtonCatalog.MaxButtons + 3)
            .Select(index => CreateButton($"B{index}", icon: $"G{index}"))
            .ToList();

        var renderable = FloatingWindowButtonCatalog.GetRenderableButtons(buttons);

        Assert.Equal(FloatingWindowButtonCatalog.MaxButtons, renderable.Count);
        Assert.Equal(buttons.Take(FloatingWindowButtonCatalog.MaxButtons), renderable);
    }

    [Fact]
    public void GetRenderableButtons_SkipsMainPageButtonWithoutTarget()
    {
        var noTarget = CreateButton("点名", icon: "A");
        noTarget.ActionTarget = string.Empty;
        var withTarget = CreateButton("点名", icon: "A");
        withTarget.ActionKind = FloatingWindowButtonActionKind.MainPage;
        withTarget.ActionTarget = "main.rollCall";

        var renderable = FloatingWindowButtonCatalog.GetRenderableButtons([noTarget, withTarget]);

        Assert.Equal([withTarget], renderable);
    }

    [Fact]
    public void Validate_ReportsLabelAndIconProblems()
    {
        var button = CreateButton(string.Empty, icon: "A");
        Assert.Equal(FloatingWindowButtonValidationError.LabelRequired,
            FloatingWindowButtonCatalog.Validate(button));

        button = CreateButton(new string('长', FloatingWindowButtonCatalog.MaxLabelLength + 1), icon: "A");
        Assert.Equal(FloatingWindowButtonValidationError.LabelTooLong,
            FloatingWindowButtonCatalog.Validate(button));

        button = CreateButton("名称");
        Assert.Equal(FloatingWindowButtonValidationError.IconMissing,
            FloatingWindowButtonCatalog.Validate(button));

        button = CreateButton("名称");
        button.IconImage = "../escape.png";
        Assert.Equal(FloatingWindowButtonValidationError.IconImageInvalid,
            FloatingWindowButtonCatalog.Validate(button));
    }

    [Theory]
    [InlineData("https://example.org/", FloatingWindowButtonValidationError.None)]
    [InlineData("http://example.org/", FloatingWindowButtonValidationError.None)]
    [InlineData("ftp://example.org/", FloatingWindowButtonValidationError.TargetInvalidUri)]
    [InlineData("file:///C:/x.txt", FloatingWindowButtonValidationError.TargetInvalidUri)]
    [InlineData("", FloatingWindowButtonValidationError.TargetRequired)]
    public void Validate_UriRequiresHttpOrHttps(string target, FloatingWindowButtonValidationError expected)
    {
        var button = CreateButton("网址", icon: "A", kind: FloatingWindowButtonActionKind.Uri, target: target);

        Assert.Equal(expected, FloatingWindowButtonCatalog.Validate(button));
    }

    [Fact]
    public void Validate_PathRequiresFullyQualifiedTarget()
    {
        var relative = CreateButton("文件", icon: "A", kind: FloatingWindowButtonActionKind.Path,
            target: "notes.txt");
        var absolute = CreateButton("文件", icon: "A", kind: FloatingWindowButtonActionKind.Path,
            target: Path.Combine(Path.GetTempPath(), "notes.txt"));

        Assert.Equal(FloatingWindowButtonValidationError.TargetInvalidPath,
            FloatingWindowButtonCatalog.Validate(relative));
        Assert.Equal(FloatingWindowButtonValidationError.None,
            FloatingWindowButtonCatalog.Validate(absolute));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("icon.PNG", true)]
    [InlineData("../icon.png", false)]
    [InlineData("sub/icon.png", false)]
    [InlineData("icon.jpg", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void IsSafeImageName_OnlyAcceptsBarePngFileNames(string value, bool expected)
    {
        Assert.Equal(expected, FloatingWindowButtonCatalog.IsSafeImageName(value));
    }

    [Fact]
    public void CloneAsNew_ClearsImageAndRegeneratesIdentity()
    {
        var source = CreateButton("教室", icon: "A", kind: FloatingWindowButtonActionKind.Uri,
            target: "https://example.org/");
        source.IconImage = "icon.png";

        var clone = source.CloneAsNew();

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Empty(clone.IconImage);
        Assert.Equal("教室副本", clone.Label);
        Assert.Equal(source.ActionKind, clone.ActionKind);
        Assert.Equal(source.ActionTarget, clone.ActionTarget);
    }

    [Theory]
    [InlineData(FloatingWindowButtonActionKind.MainPage, "main.rollCall")]
    [InlineData(FloatingWindowButtonActionKind.SettingsPage, "settings.overview")]
    [InlineData(FloatingWindowButtonActionKind.Uri, "")]
    public void GetDefaultTarget_ReturnsPageDefaults(FloatingWindowButtonActionKind kind, string expected)
    {
        Assert.Equal(expected, FloatingWindowButtonCatalog.GetDefaultTarget(kind));
    }

    [Fact]
    public void GetSuggestedIcon_ReturnsNonEmptyGlyphForEveryAction()
    {
        foreach (var kind in Enum.GetValues<FloatingWindowButtonActionKind>())
            Assert.False(string.IsNullOrEmpty(FloatingWindowButtonCatalog.GetSuggestedIcon(kind)));
    }

    [Fact]
    public void DesktopEntryIconReader_ReadsOnlyDesktopEntrySection()
    {
        const string content = """
                               [Desktop Entry]
                               # comment
                               Name=Example
                               Icon = "/usr/share/icons/example.png"
                               Exec=env FOO=bar "/opt/example/example" --flag

                               [Desktop Action new]
                               Icon=ignored.png
                               """;

        Assert.Equal("/usr/share/icons/example.png", DesktopEntryIconReader.ReadIconValue(content));
        Assert.Equal("/opt/example/example", DesktopEntryIconReader.ReadExecutable(content));
        Assert.Equal("Example", DesktopEntryIconReader.ReadKeyValue(content, "Name"));
        Assert.Null(DesktopEntryIconReader.ReadKeyValue(content, "Missing"));
    }

    [Fact]
    public void DesktopEntryIconReader_ReturnsNullForEmptyInput()
    {
        Assert.Null(DesktopEntryIconReader.ReadIconValue(null));
        Assert.Null(DesktopEntryIconReader.ReadExecutable("   "));
    }

    [Fact]
    public void IcnsPngReader_ReturnsLargestEmbeddedPng()
    {
        var small = BuildPng(payloadChunkSize: 0);
        var large = BuildPng(payloadChunkSize: 128);
        var icns = BuildIcns(small, large);

        var result = IcnsPngReader.FindLargestPng(icns);

        Assert.NotNull(result);
        Assert.Equal(large, result);
    }

    [Fact]
    public void IcnsPngReader_RejectsNonIcnsInput()
    {
        Assert.Null(IcnsPngReader.FindLargestPng(Encoding.ASCII.GetBytes("not an icns file")));
        Assert.Null(IcnsPngReader.FindLargestPng([]));
    }

    private static FloatingWindowButtonConfig CreateButton(
        string label,
        string icon = "",
        bool enabled = true,
        FloatingWindowButtonActionKind kind = FloatingWindowButtonActionKind.MainPage,
        string target = "main.rollCall")
    {
        return new FloatingWindowButtonConfig
        {
            Enabled = enabled,
            Label = label,
            IconGlyph = icon,
            ActionKind = kind,
            ActionTarget = target
        };
    }

    private static byte[] BuildPng(int payloadChunkSize)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        WriteChunk(stream, "IHDR", new byte[13]);
        if (payloadChunkSize > 0)
            WriteChunk(stream, "tEXt", new byte[payloadChunkSize]);
        WriteChunk(stream, "IEND", []);

        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        byte[] header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        Encoding.ASCII.GetBytes(type, header.AsSpan(4));
        stream.Write(header);
        stream.Write(payload);
        stream.Write(new byte[4]);
    }

    private static byte[] BuildIcns(params byte[][] pngs)
    {
        using var stream = new MemoryStream();
        stream.Write("icns"u8);
        stream.Write(new byte[4]);

        foreach (var png in pngs)
        {
            byte[] header = new byte[8];
            Encoding.ASCII.GetBytes("icp4", header.AsSpan(0));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)(png.Length + 8));
            stream.Write(header);
            stream.Write(png);
        }

        var data = stream.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), (uint)data.Length);
        return data;
    }
}
