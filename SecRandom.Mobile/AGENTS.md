# SecRandom.Mobile/ AGENTS.md

## Scope

This project is an independent Android/iOS Avalonia SingleView shell. It must not reference the desktop `SecRandom` application project or reuse its `App`, `BuildHost()`, desktop windows, desktop data-path assumptions, or desktop hosted services.

## Lifecycle

- Default builds target neutral `net10.0` as a library so desktop development does not require mobile workloads.
- `BuildMobile=true` enables `net10.0-android` and `net10.0-ios`; `MobileEntryPoint.cs` sets `PlatformStartupContext` before `MobileApp` is initialized.
- `MobileApp` owns the minimal mobile Host and assigns `MobileRootView` to the SingleView lifetime. Keep its registrations limited to features actually supported on mobile.
- `MobilePlatformServiceRoot` must explicitly report unsupported desktop window, tray, shortcut, UIAccess, URL registration, and background-residency capabilities.

## UI And Resources

- The current root is a read-only availability surface, not a reduced desktop shell. Do not add fake navigation, inactive desktop controls, or desktop-only settings.
- Mobile strings live in `Langs/Mobile/`; preserve the base resx/designer registration pattern in the project file.
- The shared app icon is linked as an Avalonia resource. Do not introduce a second product identity without an explicit mobile design decision.
- Android launcher metadata uses application ID `top.sectl.secrandom.mobile`, visible title `SecRandom`, and the shared `AppLogo.png` as its `mipmap/app_logo` resource.

## Validation

- Neutral build: `dotnet build SecRandom.Mobile/SecRandom.Mobile.csproj -c Release`.
- Android: install the `android` workload, Android SDK API 36/build-tools, and JDK 21; then restore/build with `-p:BuildMobile=true -f net10.0-android`. When the SDK/JDK are not in the environment, pass `AndroidSdkDirectory` and `JavaSdkDirectory` explicitly.
- iOS: on macOS install the `ios` workload, then restore/build with `-p:BuildMobile=true -f net10.0-ios`.
