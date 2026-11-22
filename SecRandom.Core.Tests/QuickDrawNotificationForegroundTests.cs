using System.Reflection;
using Avalonia.Media;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Presentation;
using SecRandom.Services.Draw;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Core.Tests;

public sealed class QuickDrawNotificationForegroundTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom.NotificationInk", Guid.NewGuid().ToString("N"));
    private readonly DrawAudioService _audio = new(null!, NullLogger<DrawAudioService>.Instance);

    public QuickDrawNotificationForegroundTests()
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [_dataRoot]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void NewStudentFramesUseNotificationThemeInsteadOfAppThemeOrAccent(bool darkAppTheme, bool useAccent)
    {
        using var viewModel = CreateViewModel(darkAppTheme, useAccent);
        var theme = AdaptiveBackdropResolver.FromAppTheme(!darkAppTheme);
        viewModel.ApplyNotificationTheme(theme);

        // Both rolling frames and the final student result replace this collection through the same path.
        ReplaceStudentResults(viewModel, "预览学生");
        AssertInk(viewModel, theme.Foreground);
        ReplaceStudentResults(viewModel, "最终学生");
        AssertInk(viewModel, theme.Foreground);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreparingThemeReplacesTheInkLeftByPageCreation(bool initialDarkInk)
    {
        using var viewModel = CreateViewModel(darkAppTheme: initialDarkInk);
        // The page initially supplies app-theme ink before desktop sampling has completed.
        viewModel.ApplyNotificationInk(new SolidColorBrush(initialDarkInk ? Colors.Black : Colors.White));
        ReplaceStudentResults(viewModel, "原有学生");

        var theme = AdaptiveBackdropResolver.FromAppTheme(initialDarkInk);
        viewModel.ApplyNotificationTheme(theme);
        AssertInk(viewModel, theme.Foreground);
        ReplaceStudentResults(viewModel, "首次显示");
        AssertInk(viewModel, theme.Foreground);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitCardInkSurvivesNewStudentAndStringResults(bool darkInk)
    {
        using var viewModel = CreateViewModel(darkAppTheme: darkInk);
        var ink = darkInk ? Colors.Black : Colors.White;
        viewModel.ApplyNotificationInk(new SolidColorBrush(ink));

        ReplaceStudentResults(viewModel, "学生");
        AssertInk(viewModel, ink);
        viewModel.ShowNotificationPreview(["预览"], preserveQuickDrawResult: false);
        AssertInk(viewModel, ink);
        viewModel.ShowNotificationResult(["结果"], 5, animate: false, preserveQuickDrawResult: false);
        AssertInk(viewModel, ink);
    }

    [Fact]
    public void ReusedResultsRecolorAndClearingNotificationRestoresAccent()
    {
        using var viewModel = CreateViewModel(darkAppTheme: true);
        ReplaceStudentResults(viewModel, "保留学生");
        AssertInk(viewModel, Colors.Yellow);
        var original = Assert.Single(viewModel.ResultItems);

        viewModel.ApplyNotificationTheme(AdaptiveBackdropResolver.FromAppTheme(isDarkTheme: false));
        viewModel.ShowNotificationResult(["不替换"], 5, animate: false, preserveQuickDrawResult: true);
        AssertInk(viewModel, Colors.Black);
        Assert.Equal(original.DisplayText, Assert.Single(viewModel.ResultItems).DisplayText);
        Assert.Equal(original.Tags, Assert.Single(viewModel.ResultItems).Tags);

        viewModel.ClearNotificationPresentation();
        AssertInk(viewModel, Colors.Yellow);
        ReplaceStudentResults(viewModel, "手动闪抽");
        AssertInk(viewModel, Colors.Yellow);
    }

    [Fact]
    public void QuickDrawResultsFollowTheDefaultStyleAndRefreshWhenItChanges()
    {
        using var viewModel = CreateViewModel(darkAppTheme: true);
        var config = viewModel.Config;
        ReplaceStudentResults(viewModel, "学生");

        Assert.False(Assert.Single(viewModel.ResultItems).IsCardStyle);

        config.DefaultDrawSettings.DisplayStyle = DisplayStyleMode.Card;
        Assert.True(Assert.Single(viewModel.ResultItems).IsCardStyle);

        config.QuickDrawSettings.OverrideDisplaySettings = true;
        Assert.False(Assert.Single(viewModel.ResultItems).IsCardStyle);
        config.QuickDrawSettings.DisplayStyle = DisplayStyleMode.Card;
        Assert.True(Assert.Single(viewModel.ResultItems).IsCardStyle);

        config.QuickDrawSettings.OverrideDisplaySettings = false;
        Assert.True(Assert.Single(viewModel.ResultItems).IsCardStyle);

        config.DefaultDrawSettings.DisplayStyle = DisplayStyleMode.Default;
        Assert.False(Assert.Single(viewModel.ResultItems).IsCardStyle);
    }

    private QuickDrawPageViewModel CreateViewModel(bool darkAppTheme, bool useAccent = true)
    {
        var config = new MainConfigModel();
        config.Appearance.Theme = darkAppTheme ? ThemeMode.Dark : ThemeMode.Light;
        config.QuickDrawSettings.DefaultClass = string.Empty;
        config.QuickDrawSettings.AlgorithmId = "builtin.random";
        config.DefaultDrawSettings.AnimationColorTheme = useAccent ? AnimationColorThemeMode.Fixed : AnimationColorThemeMode.None;
        config.DefaultDrawSettings.AnimationFixedColor = Colors.Yellow;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        // Sampling/commit services are unused by presentation; disposal only stops the idle audio service.
        return new QuickDrawPageViewModel(handler, null!, null!, null!, null!, _audio,
            NullLogger<QuickDrawPageViewModel>.Instance, null!, null!, null!, null!, null!);
    }

    private static void ReplaceStudentResults(QuickDrawPageViewModel viewModel, string name)
    {
        var method = typeof(QuickDrawPageViewModel).GetMethod("ReplaceResults", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException("Student result replacement was not found.");
        method.Invoke(viewModel, new object[] { new Student[] { new() { Name = name, Id = "13", Tags = "标签" } } });
    }

    private static void AssertInk(QuickDrawPageViewModel viewModel, Color expected)
    {
        var item = Assert.Single(viewModel.ResultItems);
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(item.TextBrush).Color);
    }

    public void Dispose()
    {
        _audio.Dispose();
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Utils.{name} was not found.");

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
