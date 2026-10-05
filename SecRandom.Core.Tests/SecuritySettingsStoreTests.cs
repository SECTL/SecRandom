using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Security;

namespace SecRandom.Core.Tests;

/// <summary>
///     安全设置已经从 settings.json 搬进 <c>data/config/security/settings.json</c> 的加密信封里。
///     这一组测试钉住的是搬家的**理由**：字段不能明文可改，改坏或删掉它也不能变成关掉防护的办法，
///     而旧版留在 settings.json 里的那一段只允许被迁移一次。
/// </summary>
public sealed class SecuritySettingsStoreTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(), "SecRandom", "security-settings-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void 保存后重新读取_每个字段都原样回来()
    {
        var directory = NewDirectory();
        var store = CreateStore(directory);
        store.Data.SecurityEnabled = true;
        store.Data.ProtectExit = true;
        store.Data.SudoModeDurationSeconds = 123;
        store.Save();
        store.Data.SudoModeDurationSeconds = 5;

        var reloaded = CreateStore(directory);

        Assert.True(reloaded.Data.SecurityEnabled);
        Assert.True(reloaded.Data.ProtectExit);
        Assert.Equal(5, reloaded.Data.SudoModeDurationSeconds);
        Assert.False(reloaded.IsIntegrityCompromised);
    }

    [Fact]
    public void 磁盘上只有密文_字段名不出现在文件里()
    {
        var directory = NewDirectory();
        var store = CreateStore(directory);
        store.Data.SecurityEnabled = true;
        store.Data.ProtectExit = true;
        store.Save();

        var text = File.ReadAllText(SettingsPath(directory));

        Assert.DoesNotContain("security_enabled", text, StringComparison.Ordinal);
        Assert.DoesNotContain("protect_exit", text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        Assert.True(document.RootElement.TryGetProperty("Ciphertext", out _));
        // 密钥与设置文件同住 config/security：目录本来就只对当前用户开放
        Assert.True(File.Exists(Path.Combine(directory, "security", "settings.key")));
    }

    [Fact]
    public void 文件被改过_不采用里面的值而是按最严的一档生效()
    {
        var directory = NewDirectory();
        var store = CreateStore(directory);
        // 原来只关掉了退出保护：它正是"直接改字段"最想改的那一个
        store.Data.SecurityEnabled = true;
        store.Data.ProtectExit = false;
        store.Save();

        CorruptCiphertext(SettingsPath(directory));

        var tampered = CreateStore(directory);

        Assert.True(tampered.IsIntegrityCompromised);
        Assert.True(tampered.Data.SecurityEnabled);
        Assert.True(tampered.Data.ProtectExit);
        Assert.True(tampered.Data.ProtectRollCallStart);
    }

    [Fact]
    public void 配过安全密码却删掉了设置文件_同样按最严的一档生效()
    {
        var directory = NewDirectory();
        // 全新安装先跑一次（没有安全密码，写出的是默认设置），之后用户才配上密码
        var store = CreateStore(directory);
        Assert.False(store.Data.SecurityEnabled);

        store.Data.SecurityEnabled = true;
        store.Data.SudoModeDurationSeconds = 300;
        store.Save();
        var credentials = CreateCredentials(directory);
        File.Delete(SettingsPath(directory));

        var afterDeletion = CreateStore(directory, credentials);

        Assert.True(afterDeletion.IsIntegrityCompromised);
        Assert.True(afterDeletion.Data.SecurityEnabled);
        Assert.True(afterDeletion.Data.ProtectExit);
        // 删掉的是"这台机器上的那份设置"，不是"这台机器没有安全设置"：Sudo 时长也回到默认
        Assert.Equal(20, afterDeletion.Data.SudoModeDurationSeconds);
    }

    [Fact]
    public void 全新安装_写出默认设置且防护是关的()
    {
        var directory = NewDirectory();

        var store = CreateStore(directory);

        Assert.False(store.IsIntegrityCompromised);
        Assert.False(store.Data.SecurityEnabled);
        Assert.True(File.Exists(SettingsPath(directory)));
    }

    [Fact]
    public void 旧版明文设置_只迁移一次并把明文副本从settings_json里去掉()
    {
        var directory = NewDirectory();
        var configPath = WriteLegacySettingsFile(directory, """
            { "security_enabled": true, "protect_exit": true, "sudo_mode_duration_seconds": 42 }
            """);
        var handler = CreateHandler(configPath);

        var store = CreateStore(directory, credentials: null, handler: handler);

        Assert.True(store.Data.SecurityEnabled);
        Assert.True(store.Data.ProtectExit);
        Assert.Equal(42, store.Data.SudoModeDurationSeconds);
        Assert.True(File.Exists(SettingsPath(directory)));
        // 迁移过后 settings.json 里不再留一份能改的明文安全设置
        Assert.DoesNotContain("security_settings", File.ReadAllText(configPath), StringComparison.Ordinal);
    }

    [Fact]
    public void 迁移之后_再往settings_json里写明文副本也不会生效()
    {
        var directory = NewDirectory();
        var configPath = WriteLegacySettingsFile(directory, """
            { "security_enabled": true, "protect_exit": true }
            """);
        _ = CreateStore(directory, credentials: null, handler: CreateHandler(configPath));

        // 有人把旧版那一段重新写回去：加密文件已经存在，它只能被当成旧版残留
        var node = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        node["security_settings"] = JsonNode.Parse("""{ "security_enabled": false }""");
        File.WriteAllText(configPath, node.ToJsonString());
        var handler = CreateHandler(configPath);

        var reopened = CreateStore(directory, credentials: null, handler: handler);

        Assert.True(reopened.Data.SecurityEnabled);
        Assert.True(reopened.Data.ProtectExit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }

    private string NewDirectory()
    {
        var directory = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SettingsPath(string directory) =>
        Path.Combine(directory, "security", "settings.json");

    private static SecuritySettingsStore CreateStore(
        string directory,
        SecurityCredentialStore? credentials = null,
        MainConfigHandler? handler = null) =>
        new(
            SettingsPath(directory),
            handler,
            credentials,
            NullLogger<SecuritySettingsStore>.Instance);

    private static SecurityCredentialStore CreateCredentials(string directory)
    {
        var store = new SecurityCredentialStore(
            Path.Combine(directory, "security", "credentials.json"),
            CredentialKdfParameters.Test);
        using var context = store.Create("secret1");
        store.Save(context);
        return store;
    }

    private static MainConfigHandler CreateHandler(string configPath) =>
        new(NullLogger<MainConfigHandler>.Instance, new JsonFileConfigService(configPath));

    /// <summary>写一份"旧版本写出来的" settings.json：里面还带着明文的安全设置。</summary>
    private static string WriteLegacySettingsFile(string directory, string securitySettings)
    {
        var configPath = Path.Combine(directory, "config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var node = JsonNode.Parse(JsonSerializer.Serialize(new MainConfigModel(), ConfigServiceBase.JsonOptions))!.AsObject();
        node["security_settings"] = JsonNode.Parse(securitySettings);
        File.WriteAllText(configPath, node.ToJsonString());
        return configPath;
    }

    /// <summary>把密文的第一段字节翻转，等价于"有人拿编辑器动了这个文件"。</summary>
    private static void CorruptCiphertext(string path)
    {
        // 走 JsonNode 改写而不是字符串替换：默认编码器会把 base64 里的 + / 写成转义序列
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var ciphertext = Convert.FromBase64String(node["Ciphertext"]!.GetValue<string>());
        ciphertext[0] ^= 0xFF;
        node["Ciphertext"] = Convert.ToBase64String(ciphertext);
        File.WriteAllText(path, node.ToJsonString());
    }

    private sealed class JsonFileConfigService(string path) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => File.Exists(path);

        public override T LoadConfig<T>(T fallback) =>
            File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback
                : fallback;

        public override void SaveConfig<T>(T config) =>
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));

        public override void DeleteConfig<T>(T config)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
