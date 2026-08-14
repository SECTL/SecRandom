# SecRandom Example Plugin

The plugin entry point is the single non-abstract type derived from `PluginBase` in the assembly named by `manifest.yml`.

## Usage

Build the project to create `srpx/SecRandom.ExamplePlugin.srpx`. Place that package in `data/cache/plugin-packages` and restart the desktop application.

## Development

- Plugins reference `SecRandom.Core` through `SecRandom.PluginSdk`.
- Published plugins use `PackageReference` for `SecRandom.PluginSdk` with `ExcludeAssets="runtime;native"`.
- External package references stay copy-local and are included in the SRPX package.

```xml
<PackageReference Include="SecRandom.PluginSdk" Version="1.0.0">
  <ExcludeAssets>runtime;native</ExcludeAssets>
</PackageReference>
```

## API

| API | Description |
|-----|-------------|
| `PluginBase.Initialize` | Registers services and Core extensions before the Host is built |
| `IPluginManager` | Lists plugins and manages enablement |
| `PluginInfo` | Plugin metadata and load state |

> This README also demonstrates the markdown rendering used by the plugin settings page.
