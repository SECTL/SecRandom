using SecRandom.Services.RosterTransfer;

namespace SecRandom.Views.SettingsPages.ListManagement;

public enum RosterExportMode
{
    File,
    QuickQr,
    OfflineQr,
    SessionCode
}

public sealed record RosterExportModeOption(RosterExportMode Mode, string Label);

public enum RosterImportMode
{
    ExcelCsv,
    QuickQr,
    OfflineQr,
    SessionCode
}

public sealed record RosterImportModeOption(RosterImportMode Mode, string Label);
