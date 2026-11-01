using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

/// <summary>
///     History record group settings.
/// </summary>
/// <remarks>
///     <para>
///         A history record group partitions a profile's draw history into named usage buckets (for example 上课提问 and
///         期末抽奖) so that one usage's records do not drag down a member's weight in another. When enabled, the main
///         pages offer a group selector; while a group is picked, weights only consider that group's records and the new
///         records are written into it. Picking a group never asks for a security password — it is a draw scope, not a
///         protected operation.
///     </para>
///     <para>
///         The switch is off by default because enabling it changes what the weights mean, and because an older build
///         does not know the record field: a downgrade keeps the records but silently drops their group markers, which
///         turns every group-scoped record back into global history. Nothing is lost, but the user must be told.
///     </para>
/// </remarks>
public partial class HistoryGroupSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _enabled;
}
