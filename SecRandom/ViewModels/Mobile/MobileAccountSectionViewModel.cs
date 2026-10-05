using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Helpers;
using SecRandom.Services.Auth;

namespace SecRandom.ViewModels.Mobile;

/// <summary>
///     移动端设置页顶部的账号区：登录 / 已登录（头像 + 昵称）/ 退出登录。
/// </summary>
/// <remarks>
///     <para>
///         为什么必须放在设置页顶部：手机端没有桌面那条标题栏账号下拉，设置页是唯一"账号相关"的地方。
///         不放在显眼处，用户遇到"未登录"只会看到一个用不了的功能，却找不到登录入口。
///     </para>
///     <para>
///         为什么订阅 <see cref="SectlAuthService.StateChanged" /> 而不是构造时读一次：
///         登录、退出、凭据失效都发生在**页面已经显示之后**（OAuth 要跳浏览器、令牌还会自己过期）。
///         读一次就固定的话，用户登录回来仍看到"未登录"，只能靠重启应用——那正是要修掉的那类问题。
///     </para>
/// </remarks>
public sealed partial class MobileAccountSectionViewModel : ObservableObject, IDisposable
{
    private readonly SectlAuthService _auth;
    private readonly ILogger<MobileAccountSectionViewModel>? _logger;
    private Bitmap? _avatar;
    private bool _subscribed;

    public MobileAccountSectionViewModel(
        SectlAuthService auth,
        ILogger<MobileAccountSectionViewModel>? logger = null)
    {
        _auth = auth;
        _logger = logger;
        Apply();
        Subscribe();
    }

    [ObservableProperty] private bool _isSignedIn;

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _displayName = string.Empty;

    [ObservableProperty] private string _accountId = string.Empty;

    [ObservableProperty] private string _initial = string.Empty;

    /// <summary>凭据已失效（刷新也救不回来）：显示"重新登录"而不是普通登录。</summary>
    [ObservableProperty] private bool _requiresReauthorization;

    public Bitmap? Avatar => _avatar;

    public bool HasAvatar => _avatar is not null;

    public bool ShowInitial => IsSignedIn && !HasAvatar;

    public bool ShowPlaceholder => !IsSignedIn && !HasAvatar;

    /// <summary>
    ///     昵称 → 账号 ID 的回落（纯函数，便于单测）。
    /// </summary>
    /// <remarks>
    ///     服务端可能只回 ID，也可能用户资料还没加载完（登录后是后台拉取的）。
    ///     界面绝不留空：宁可显示账号 ID，也不要一个空白行。
    /// </remarks>
    public static string ResolveDisplayName(SectlUser? user, string? tokenUserId)
    {
        var name = FirstNonBlank(user?.ResolvedUserName, user?.ResolvedUserId, tokenUserId);
        return name ?? string.Empty;
    }

    public static string ResolveAccountId(SectlUser? user, string? tokenUserId) =>
        FirstNonBlank(user?.ResolvedUserId, tokenUserId) ?? string.Empty;

    [RelayCommand]
    private async Task SignInAsync()
    {
        IsBusy = true;
        try
        {
            await _auth.SignInAsync();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "移动端账号登录失败。");
        }
        finally
        {
            IsBusy = false;
            Apply();
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        IsBusy = true;
        try
        {
            await _auth.SignOutAsync();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "移动端账号退出失败。");
        }
        finally
        {
            IsBusy = false;
            Apply();
        }
    }

    public void Dispose() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed)
            return;

        _auth.StateChanged += OnAuthStateChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _auth.StateChanged -= OnAuthStateChanged;
        _subscribed = false;
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        // StateChanged 可能从后台线程发出（令牌刷新、退出），位图与绑定都要回到 UI 线程再改。
        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void Apply()
    {
        IsSignedIn = _auth.IsSignedIn;
        RequiresReauthorization = _auth.RequiresReauthorization;
        DisplayName = ResolveDisplayName(_auth.User, _auth.Token?.UserId);
        AccountId = ResolveAccountId(_auth.User, _auth.Token?.UserId);
        Initial = AvatarInitialResolver.Resolve(DisplayName, null);
        UpdateAvatar();
    }

    private void UpdateAvatar()
    {
        Bitmap? avatar = null;
        if (IsSignedIn && _auth.AvatarBytes is { Length: > 0 } bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                avatar = new Bitmap(stream);
            }
            catch (Exception exception)
            {
                // 头像解码失败只是少一张图，绝不能连累账号区显示。
                _logger?.LogDebug(exception, "移动端账号头像解码失败。");
                avatar = null;
            }
        }

        // 旧位图不主动 Dispose：绑定可能还在渲染它，换图只是把引用换成新的（头像很小，且一天换不了几次）。
        _avatar = avatar;
        OnPropertyChanged(nameof(Avatar));
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(ShowInitial));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
