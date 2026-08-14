using SecRandom.Core.Abstraction.Services;
using SecRandom.Services.Plugins;

namespace SecRandom.Core.Tests;

public sealed class FloatingWindowButtonRegistryTests
{
    [Fact]
    public void Register_AddsAndRaisesChanged()
    {
        var registry = new FloatingWindowButtonRegistry();
        var changeCount = 0;
        registry.Changed += (_, _) => changeCount++;
        var button = new FloatingWindowButtonDescriptor("plugin.test.button", "AppsFilled", "Test", () => { });

        registry.Register(button);

        Assert.Single(registry.Buttons);
        Assert.Equal("plugin.test.button", registry.Buttons[0].Id);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void Register_SameIdReplacesExisting()
    {
        var registry = new FloatingWindowButtonRegistry();
        var first = new FloatingWindowButtonDescriptor("plugin.test.button", "AppsFilled", "First", () => { });
        var second = new FloatingWindowButtonDescriptor("plugin.test.button", "BeakerFilled", "Second", () => { });

        registry.Register(first);
        registry.Register(second);

        Assert.Single(registry.Buttons);
        Assert.Equal("Second", registry.Buttons[0].Label);
    }

    [Fact]
    public void Unregister_RemovesAndRaisesChanged()
    {
        var registry = new FloatingWindowButtonRegistry();
        registry.Register(new FloatingWindowButtonDescriptor("plugin.test.button", "AppsFilled", "Test", () => { }));
        var changeCount = 0;
        registry.Changed += (_, _) => changeCount++;

        Assert.True(registry.Unregister("plugin.test.button"));
        Assert.Empty(registry.Buttons);
        Assert.Equal(1, changeCount);

        Assert.False(registry.Unregister("plugin.test.button"));
    }

    [Fact]
    public void Register_NullIdThrows()
    {
        var registry = new FloatingWindowButtonRegistry();
        var button = new FloatingWindowButtonDescriptor(" ", "AppsFilled", "Test", () => { });

        Assert.Throws<ArgumentException>(() => registry.Register(button));
    }
}
