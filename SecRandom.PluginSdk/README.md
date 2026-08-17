# SecRandom Plugin SDK

SDK for developing in-process SecRandom plugins. Plugins reference `SecRandom.Core` through this SDK and register services, settings pages, main pages, or other Core extensions from their entry point.

## API version

- `manifest.yml` `apiVersion` declares which host API the plugin targets.
- The host rejects plugins whose `apiVersion` major is below `PluginApiVersions.Current.Major` (currently `3`).
- `apiVersion` follows the application major version and must be bumped with it.
- `version` in `manifest.yml` is the plugin's own version and is independent of `apiVersion`.

Example manifest:

```yaml
id: secrandom.example
name: SecRandom 示例插件
entranceAssembly: SecRandom.ExamplePlugin.dll
apiVersion: 3.0.0
version: 1.0.0
author: SECTL
```

## Referencing the SDK

Published plugins reference the SDK package and exclude its runtime assets so the host supplies Core and its dependencies:

```xml
<PackageReference Include="SecRandom.PluginSdk" Version="3.0.0">
  <ExcludeAssets>runtime;native</ExcludeAssets>
</PackageReference>
```

The repository template defaults `UseLocalPluginSdk=true` so solution builds work before the SDK is published; set `UseLocalPluginSdk=false` with a NuGet source that contains `SecRandom.PluginSdk` to exercise the release packaging path.

## Host integration APIs

`IExternalStudentDrawService` lets a transport plugin request a host-managed student draw
without depending on desktop application's internal service types. The host owns drawing,
temporary records, history, security, and notifications; the plugin owns its transport.

## Building a plugin package

Set `<CreateSrpx>true</CreateSrpx>` to produce `srpx/<ProjectName>.srpx` after every build. The package is a ZIP whose root contains `manifest.yml`, the entrance assembly, and any external package dependencies. Place it in `data/cache/plugin-packages` and restart the desktop application to install.
