using System.Globalization;
using SecRandom.Services.RosterTransfer;
using SecRandom.Shared.Models.Profile;
using SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer;
using LR = SecRandom.Langs.SettingsPages.ListManagement.LotteryList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public sealed class LotteryListExportView : RosterListExportView
{
    public LotteryListExportView(string listName, IReadOnlyList<Prize> prizes)
        : base(listName,
            new RosterTransferDocument(1, RosterTransferKind.Prizes, $"{listName}.secrandom-roster.json", prizes.Select(prize =>
                new RosterTransferRow(prize.Exists, prize.Id, prize.Name,
                    prize.Weight.ToString(CultureInfo.InvariantCulture), prize.Count.ToString(CultureInfo.InvariantCulture), prize.Tags)).ToArray()),
            prizes.Select(prize => new Dictionary<string, object?>
            {
                [GetField("C_PrizeId")] = prize.Id,
                [GetField("C_Name")] = prize.Name,
                [GetField("C_Weight")] = prize.Weight,
                [GetField("C_Count")] = prize.Count,
                [GetField("C_Tags")] = prize.Tags,
                [GetField("C_Exists")] = prize.Exists
            }).ToArray(),
            RosterTransferText.Get)
    {
    }

    private static string GetField(string name) => LR.ResourceManager.GetString(name, LR.Culture) ?? name;
}
