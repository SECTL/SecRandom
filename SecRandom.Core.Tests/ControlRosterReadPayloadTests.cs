using System.Text.Json;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     <c>roster.read</c> 的返回载荷：控制台要照着它复刻本机列表页（含"标签"列）。
/// </summary>
/// <remarks>
///     名单里带着学生姓名与标签，是权限最高的一条只读通道，所以这里的断言同时钉住两件事：
///     **字段名是控制台消费的那一套**，以及**"没有值"必须真的是 <c>null</c>**——
///     空串和空数组到了控制台就是一行空白单元格，管理员分不清是"这个人没有标签"还是"设备没读出来"。
/// </remarks>
public sealed class ControlRosterReadPayloadTests
{
    // ---------------------------------------------------------------- 投影：列表里的成员 → 协议里的成员

    [Fact]
    public void 成员投影_学生的字段与标签()
    {
        var student = new Student
        {
            Id = " 01 ",
            Name = " 张三 ",
            Gender = " 男 ",
            Group = " A组 ",
            Tags = " 三好学生, 组长 ",
            Exists = true
        };

        var member = ControlRosterMemberPayload.FromStudent(student);

        Assert.Equal("01", member.Id);
        Assert.Equal("张三", member.Name);
        Assert.Equal("男", member.Gender);
        Assert.Equal("A组", member.Group);
        Assert.Equal(new[] { "三好学生", "组长" }, member.Tags);
        Assert.True(member.Enabled);

        // 学生没有数量与权重：给 null 而不是 0，控制台才不会显示"数量 0"。
        Assert.Null(member.Count);
        Assert.Null(member.Weight);
    }

    [Fact]
    public void 成员投影_奖品的字段与标签()
    {
        var prize = new Prize
        {
            Id = "P1",
            Name = " 一等奖 ",
            Count = 3,
            Weight = 2.5,
            Tags = " 学习用品 ",
            Exists = false
        };

        var member = ControlRosterMemberPayload.FromPrize(prize);

        Assert.Equal("P1", member.Id);
        Assert.Equal("一等奖", member.Name);
        Assert.Equal(3, member.Count);
        Assert.Equal(2.5, member.Weight);
        Assert.Equal(new[] { "学习用品" }, member.Tags);
        Assert.False(member.Enabled);

        // 奖品没有性别与分组。
        Assert.Null(member.Gender);
        Assert.Null(member.Group);
    }

    [Fact]
    public void 成员投影_空白字段一律给null不给空串()
    {
        var student = new Student { Id = "   ", Name = "", Gender = " ", Tags = "   " };
        var member = ControlRosterMemberPayload.FromStudent(student);

        Assert.Null(member.Id);
        Assert.Null(member.Name);
        Assert.Null(member.Gender);
        Assert.Null(member.Group);
        Assert.Null(member.Tags);

        var prize = new Prize { Id = "", Name = "  ", Tags = "" };
        var prizeMember = ControlRosterMemberPayload.FromPrize(prize);

        Assert.Null(prizeMember.Id);
        Assert.Null(prizeMember.Name);
        Assert.Null(prizeMember.Tags);
    }

    /// <summary>
    ///     标签串拆成数组：去空白、丢空项、去掉重复——与名单导入规范化标签时用的是同一条规则。
    /// </summary>
    /// <remarks>
    ///     没有标签时给 <c>null</c> 而不是空数组：两者在协议里都是"没有"，但成员是这条命令里数量最多的对象，
    ///     每个成员都带上 <c>"tags":[]</c> 只会白白撑大帧（帧上限 64 KiB，见
    ///     <see cref="ControlRosterReadRequest.MaxMembersPerList" />）。
    /// </remarks>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("    ", null)]
    [InlineData("三好学生", new[] { "三好学生" })]
    [InlineData("  三好学生   组长  ", new[] { "三好学生", "组长" })]
    [InlineData("三好学生，组长；班干部", new[] { "三好学生", "组长", "班干部" })]
    [InlineData("三好学生|组长/班干部", new[] { "三好学生", "组长", "班干部" })]
    [InlineData("三好学生 三好学生", new[] { "三好学生" })]
    public void 标签_拆分后没有空白也没有空项(string? raw, string[]? expected)
    {
        Assert.Equal(expected, ControlRosterMemberPayload.NormalizeTags(raw));
    }

    // ---------------------------------------------------------------- 往返

    [Fact]
    public void 成员载荷_JSON往返保持字段名与标签()
    {
        var response = new ControlRosterReadResponse(
            ControlRosterReadRequest.Students,
            [
                new ControlRosterListPayload(
                    "高一（1）班",
                    true,
                    2,
                    2,
                    false,
                    [
                        ControlRosterMemberPayload.FromStudent(
                            new Student { Id = "01", Name = "张三", Tags = "三好学生 组长" }),
                        ControlRosterMemberPayload.FromStudent(new Student { Id = "02", Name = "李四" })
                    ])
            ]);

        var json = JsonSerializer.Serialize(response);

        // 字段名（都是 ASCII，所以能直接在原文里钉住）。
        Assert.Contains("\"roster_kind\":\"students\"", json);
        Assert.Contains("\"tags\":null", json);
        Assert.DoesNotContain("\"Tags\"", json);

        using (var document = JsonDocument.Parse(json))
        {
            var members = document.RootElement.GetProperty("lists")[0].GetProperty("members");

            Assert.Equal(
                new[] { "三好学生", "组长" },
                members[0].GetProperty("tags").EnumerateArray().Select(item => item.GetString()).ToArray());

            // 没有标签的成员必须真的是 null，而不是 [] 或 [""]。
            Assert.Equal(JsonValueKind.Null, members[1].GetProperty("tags").ValueKind);
        }

        var roundTripped = JsonSerializer.Deserialize<ControlRosterReadResponse>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(ControlRosterReadRequest.Students, roundTripped!.RosterKind);

        var sent = response.Lists[0].Members;
        var received = roundTripped.Lists[0].Members;

        Assert.Equal(sent.Count, received.Count);
        for (var index = 0; index < sent.Count; index++)
        {
            Assert.Equal(sent[index].Id, received[index].Id);
            Assert.Equal(sent[index].Name, received[index].Name);
            Assert.Equal(sent[index].Enabled, received[index].Enabled);
            Assert.Equal(Render(sent[index].Tags), Render(received[index].Tags));
        }

        Assert.Null(received[1].Tags);
    }

    [Fact]
    public void 成员载荷_奖品的数量与权重原样往返()
    {
        var response = new ControlRosterReadResponse(
            ControlRosterReadRequest.Prizes,
            [
                new ControlRosterListPayload(
                    "默认奖池",
                    true,
                    1,
                    1,
                    false,
                    [ControlRosterMemberPayload.FromPrize(new Prize { Id = "P1", Name = "一等奖", Count = 3, Weight = 2.5 })])
            ]);

        var roundTripped = JsonSerializer.Deserialize<ControlRosterReadResponse>(JsonSerializer.Serialize(response));

        Assert.NotNull(roundTripped);
        Assert.Equal(ControlRosterReadRequest.Prizes, roundTripped!.RosterKind);

        var member = roundTripped.Lists[0].Members[0];

        Assert.Equal(3, member.Count);
        Assert.Equal(2.5, member.Weight);
        Assert.Null(member.Gender);
        Assert.Null(member.Group);
        Assert.Null(member.Tags);
    }

    /// <summary>把标签列表摊成可逐字比较的字符串（顺带把 <c>null</c> 与空列表区分开）。</summary>
    private static string? Render(IReadOnlyList<string>? tags) =>
        tags is null ? null : string.Join('|', tags);
}
