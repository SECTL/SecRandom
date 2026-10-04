using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SecRandom.Services.Security;

/// <summary>
///     Best-effort "current user only" hardening for the credential directory that every secret file
///     under <c>data/config/security</c> shares. The whole directory is restricted once per process by
///     the credential store, and every writer in that directory re-applies the file rule on save, so
///     the cloud backup key cache cannot widen access to the security password or the TOTP seed copy.
///     Every operation is best-effort and must never make a file unreadable to the app itself: a
///     filesystem without ACLs (FAT/exFAT, some network shares) simply keeps its default permissions.
/// </summary>
internal static class SecurityPathProtection
{
    public static void RestrictDirectoryToOwner(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictDirectoryOnWindows(directory);
            return;
        }

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // 收紧目录权限是尽力而为的加固，失败不影响凭据读写
        }
    }

    /// <summary>
    ///     Windows 上默认 ACL 通常允许 Users / Authenticated Users 读取整个 data 目录，这里断开继承
    ///     并只保留当前用户（外加 SYSTEM 与 Administrators，避免目录变得无法管理）。一条规则都没能
    ///     加上时保持原样，不写出空 DACL。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void RestrictDirectoryOnWindows(string directory)
    {
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var granted = AddFullControl(security, WindowsIdentity.GetCurrent().User);
            granted |= AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
            granted |= AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            if (!granted)
                return;

            new DirectoryInfo(directory).SetAccessControl(security);
        }
        catch (Exception)
        {
            // 例如 FAT/exFAT/网络位置上没有 ACL 支持；保持原样而不是让凭据变得不可读写
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool AddFullControl(DirectorySecurity security, IdentityReference? identity)
    {
        if (identity is null)
            return false;

        try
        {
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            return true;
        }
        catch (Exception)
        {
            // 单个 SID 无法解析（例如域控上缺少内建组）时跳过，不影响其余规则
            return false;
        }
    }

    public static void RestrictFileToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // 收紧文件权限是尽力而为的加固，失败不影响凭据与免密种子的读写
        }
    }
}
