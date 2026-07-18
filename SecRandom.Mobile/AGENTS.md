# SecRandom.Mobile/ AGENTS.md

## Scope

This project is an independent Android/iOS Avalonia SingleView shell. It must not reference the desktop `SecRandom` application project or reuse its `App`, `BuildHost()`, desktop windows, desktop data-path assumptions, or desktop hosted services.

## Lifecycle

- Default builds target neutral `net10.0` as a library so desktop development does not require mobile workloads.
- `BuildMobile=true` enables `net10.0-android` and `net10.0-ios`; `MobileEntryPoint.cs` sets `PlatformStartupContext` before `MobileApp` is initialized. CI currently builds Android only; re-enable iOS validation only when its Xcode, signing, and distribution contract are defined.
- `MobileApp` owns the minimal mobile Host and assigns `MobileRootView` to the SingleView lifetime. Keep its registrations limited to features actually supported on mobile.
- `MobilePlatformServiceRoot` must explicitly report unsupported desktop window, tray, shortcut, UIAccess, URL registration, and background-residency capabilities.

## UI And Resources

- The current root is a read-only availability surface, not a reduced desktop shell. Do not add fake navigation, inactive desktop controls, or desktop-only settings.
- Mobile strings live in `Langs/Mobile/`; preserve the base resx/designer registration pattern in the project file.
- The shared app icon is linked as an Avalonia resource. Do not introduce a second product identity without an explicit mobile design decision.
- Android launcher metadata uses application ID `cn.sectl.secrandom.mobile`, visible title `SecRandom`, and the shared `AppLogo.png` as its `mipmap/app_logo` resource. Android `versionName` follows the same Git tag as desktop; release CI assigns the GitHub run number as the monotonic Android `versionCode`, so an update APK can replace an earlier build. CI renames the APK to `SecRandom-v<version>-Android-arm64.apk` after signing; a formal release requires all Android keystore secrets so future updates retain the same signer.
- Android checks the shared signed release manifest, verifies APK length/SHA-512, and hands a downloaded update to the system installer. iOS distribution and updates are deliberately deferred.

## Validation

- Neutral build: `dotnet build SecRandom.Mobile/SecRandom.Mobile.csproj -c Release`.
- Android: install the `android` workload, Android SDK API 36/build-tools, and JDK 21; then restore/build with `-p:BuildMobile=true -f net10.0-android`. When the SDK/JDK are not in the environment, pass `AndroidSdkDirectory` and `JavaSdkDirectory` explicitly.
- iOS: on macOS install the `ios` workload, then restore/build with `-p:BuildMobile=true -f net10.0-ios`.
