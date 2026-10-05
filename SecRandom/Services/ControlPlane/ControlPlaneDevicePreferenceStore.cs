using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared;

namespace SecRandom.Services.ControlPlane;

/// <summary>上次选中的教室设备（组 + 节点）。</summary>
public sealed record ControlPlaneDevicePreference(
    [property: JsonPropertyName("group_id")] string GroupId,
    [property: JsonPropertyName("node_id")] string NodeId);

/// <summary>记住"上次控制的是哪台机器"。</summary>
public interface IControlPlaneDevicePreferenceStore
{
    ControlPlaneDevicePreference? Load();

    void Save(ControlPlaneDevicePreference preference);
}

/// <summary>
///     把上次选中的设备写进 <c>data/config/control-plane/last-device.json</c>。
/// </summary>
/// <remarks>
///     <para>
///         为什么不放进 <c>settings.json</c>：它是**这台手机**的界面记忆（"我上次控制的是哪台机器"），
///         不是应用设置。放进设置文件会跟着设置导入/导出和云备份跑到别的设备上，
///         那儿根本没有同一台教室机；也会让"设置被导入"顺带改掉一个纯粹的本地偏好。
///     </para>
///     <para>
///         读取一律容错：文件缺失、被截断、内容不是 JSON、字段为空——都返回 <c>null</c>，
///         页面回落到"第一台在线的设备"。一个记不住上次选择的手机不该打不开抽取页。
///     </para>
///     <para>
///         写入沿用本项目的原子替换约定（先写临时文件，再覆盖目标）：直接覆盖时断电会留下半个文件，
///         下次启动就读不出上次的选择了。
///     </para>
/// </remarks>
public sealed class FileControlPlaneDevicePreferenceStore(string? filePath = null) : IControlPlaneDevicePreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath =
        filePath ?? Utils.GetFilePath("config", "control-plane", "last-device.json");

    public ControlPlaneDevicePreference? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;

            var preference = JsonSerializer.Deserialize<ControlPlaneDevicePreference>(
                File.ReadAllText(_filePath),
                JsonOptions);

            return string.IsNullOrWhiteSpace(preference?.GroupId) || string.IsNullOrWhiteSpace(preference.NodeId)
                ? null
                : preference;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(ControlPlaneDevicePreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preference, JsonOptions));
        File.Move(temporary, _filePath, overwrite: true);
    }
}
