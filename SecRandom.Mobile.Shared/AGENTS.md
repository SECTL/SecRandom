# SecRandom.Mobile.Shared/ AGENTS.md

## Scope

This project is the shared Android/iOS Avalonia SingleView shell library. It must not reference the desktop `SecRandom` application project or reuse its `App`, `BuildHost()`, desktop windows, desktop data-path assumptions, or desktop hosted services. The `SecRandom.Android` and `SecRandom.iOS` head projects own the platform entry points and reference this library.

## Project Layout

- This library always builds as neutral `net10.0`. Its `AssemblyName`/`RootNamespace` stay `SecRandom.Mobile` so Core `InternalsVisibleTo("SecRandom.Mobile")` and `avares://SecRandom.Mobile/` resource URIs keep working; C# namespaces stay `SecRandom.Mobile.*`.
- `BuildMobile=true` is consumed only by the `SecRandom.Android` / `SecRandom.iOS` head projects: it switches each head to `net10.0-android` / `net10.0-ios` Exe output. `MobileTargetFramework` no longer exists; build the head project directly for a platform-specific build. The heads set `PlatformStartupContext` with a `MobilePlatformServiceRoot` before `MobileApp` is initialized. CI builds Android packages and continuously compiles the unsigned iOS arm64 target. The unsigned arm64 IPA is a required input to manual release publication and is uploaded with the other release assets. iOS device distribution and in-app update delivery remain deferred.
- Platform seams live behind injectable contracts in this library: `IMobileUpdateInstaller` (with `UnsupportedMobileUpdateInstaller` fallback) abstracts update package staging/installation, and `MobilePlatformServiceRoot.StartupErrorLogger` lets the Android head attach `Android.Util.Log` startup diagnostics. The shared library must not contain `#if ANDROID` / `#if IOS` platform code.
- `MobileApp` owns the minimal mobile Host and assigns `MobileRootView` to the SingleView lifetime. `MobileRootView` owns fixed mobile chrome around an inner physical `ViewHostControl`, which is attached through `SingleViewHostProvider`; keep its registrations limited to features actually supported on mobile.
- Before building that Host, `MobileApp` calls `Utils.ConfigureMobileDataRoot()` once to select `LocalApplicationData/SecRandom/data`. The Host then calls `AddCoreRuntimeServices()` to register the host-internal Core JSON config, profile, temporary-record, lottery-availability, and draw services; it assigns `IAppHost.Host` only for transitional Core consumers and clears it during shutdown. Never reuse the desktop package-root `data/` path or add desktop hosted services.
- `MobilePlatformServiceRoot` must explicitly report unsupported desktop window, tray, shortcut, UIAccess, URL registration, and background-residency capabilities.

## UI And Resources

- The fixed bottom bar selects MVE routes: `main.rollCall`, `main.history`, `main.overview`, and `settings.mobile`. The root shell must not directly construct business pages. `MobileDrawPage`, `MobileHistoryPage`, `MobileOverviewPage`, and `MobileSettingsCatalogPage` are logical `ViewBase` sessions. Each mobile settings destination is independently registered under `settings.mobile.*`; the catalog retains exactly `通用`, `个性化`, `名单管理`, `抽取`, `备份`, `更新`, and `关于`.
- Mobile keeps the Fair/Random roll-call mode selector, but Fair must use the fixed `MobileDesktopDefaultsV1` Core policy snapshot and must not expose or consume persisted `FairDrawSettings`. The backup section must explain its unavailable system-file transaction rather than expose a fake action. Do not add desktop-only settings, tray/window controls, shortcuts, OOBE, audio/notification surfaces, or fake inactive controls.
- `MobileOnlineStatusService` is a mobile-only hosted runtime consumer of `PrivacySettings.OnlineStatusMode`: `Off` makes no network requests; `Anonymous` uses an empty UUID and no IP-derived data; `Full` uses a persisted mobile-private `data/config/device-uuid.json`. Native device types are `android` and `ios`; do not report native mobile clients as web clients.
- Mobile pages live under `Views/`; shared visual construction helpers belong in `MobileUi`/`MobileTheme`; Core services remain the reusable backend boundary.
- Mobile strings live in `Langs/Mobile/`; preserve the base resx/designer registration pattern in the project file.
- The shared app icon is linked as an Avalonia resource. Do not introduce a second product identity without an explicit mobile design decision.
- Android launcher metadata lives in the `SecRandom.Android` head: application ID `cn.sectl.secrandom.mobile`, visible title `SecRandom`, and the shared `AppLogo.png` as its `mipmap/app_logo` resource. Android `versionName` follows the same Git tag as desktop; release CI assigns the GitHub run number as the monotonic Android `versionCode`, so an update APK can replace an earlier build. CI renames the APK to `SecRandom-v<version>-Android-arm64.apk` after signing; a formal release requires all Android keystore secrets so future updates retain the same signer.
- Android checks the shared signed release manifest, verifies APK length/SHA-512, and hands a downloaded update to the system installer through the head's `AndroidUpdateInstaller`. Signed iOS IPAs are included in the shared signed release manifest for release integrity, but iOS device distribution and in-app update delivery remain deferred. The GitInfo version attributes are generated on the head assemblies, so update version detection reads the entry assembly.

## Validation

- Shared library: `dotnet build SecRandom.Mobile.Shared/SecRandom.Mobile.Shared.csproj -c Release`.
- Android: install the `android` workload, Android SDK API 36/build-tools, and JDK 21; then `dotnet build SecRandom.Android/SecRandom.Android.csproj -c Release -p:BuildMobile=true`. When the SDK/JDK are not in the environment, pass `AndroidSdkDirectory` and `JavaSdkDirectory` explicitly.
- iOS: on macOS install the `ios` workload, then `dotnet build SecRandom.iOS/SecRandom.iOS.csproj -c Release -p:BuildMobile=true`. The release signing path requires `IOS_SIGNING_CERTIFICATE_BASE64`, `IOS_SIGNING_CERTIFICATE_PASSWORD`, `IOS_SIGNING_IDENTITY`, and `IOS_PROVISIONING_PROFILE_BASE64`.
