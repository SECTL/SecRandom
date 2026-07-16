using SecRandom.Shared.Models.Profile;

namespace SecRandom.Shared.Extensions;

public static class ProfileListOrderingExtensions
{
    public static IOrderedEnumerable<Student> OrderForList(this IEnumerable<Student> students)
    {
        return OrderForList(students, student => student.Id, student => student.Name);
    }

    public static IOrderedEnumerable<Prize> OrderForList(this IEnumerable<Prize> prizes)
    {
        return OrderForList(prizes, prize => prize.Id, prize => prize.Name);
    }

    private static IOrderedEnumerable<T> OrderForList<T>(
        IEnumerable<T> items,
        Func<T, string> getId,
        Func<T, string> getName)
    {
        return items
            .OrderBy(item => string.IsNullOrWhiteSpace(getId(item)))
            .ThenBy(item => int.TryParse(getId(item), out _) ? 0 : 1)
            .ThenBy(item => int.TryParse(getId(item), out var id) ? id : int.MaxValue)
            .ThenBy(item => string.IsNullOrWhiteSpace(getId(item)) ? getName(item).Trim() : getId(item).Trim(),
                StringComparer.CurrentCultureIgnoreCase);
    }
}
