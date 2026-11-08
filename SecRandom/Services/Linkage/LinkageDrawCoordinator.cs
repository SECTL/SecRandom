using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Security;

namespace SecRandom.Services.Linkage;

/// <summary>
///     一次抽取在联动、安全与集控三个闸门下的判定结果。
/// </summary>
public enum LinkageDrawGate
{
    /// <summary>可以抽取，且不会拉起任何交互式验证。</summary>
    Allowed,

    /// <summary>本机会拉起密码/TOTP/USB 验证框。<b>集控远程命令不能用这种路径</b>（教室机可能没人）。</summary>
    RequiresLocalVerification,

    /// <summary>课程联动判定为受限（确认非上课，或数据源不可用且要求维持限制），而联动设置不允许这时候抽取。</summary>
    BlockedByClassTime,

    /// <summary>集控期望状态锁定了抽取。</summary>
    BlockedByRemoteLock
}

public sealed class LinkageDrawCoordinator(
    CourseLinkageService linkageService,
    ISecurityService securityService,
    IControlDrawGate controlDrawGate)
{
    public async Task<bool> AuthorizeAsync(
        SecurityOperation operation,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        return await AuthorizeAsync([operation], action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AuthorizeAsync(
        IReadOnlyCollection<SecurityOperation> operations,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        // 集控锁定的抽取在任何本机路径上都不放行；这里直接返回，绝不进入验证框。
        if (controlDrawGate.IsDrawLocked)
            return false;

        if (!IsLinkageRestricted)
            return await securityService.AuthorizeAsync(operations, action, cancellationToken).ConfigureAwait(false);

        if (!linkageService.Settings.VerificationRequired)
        {
            // 不允许验证绕过时这里是课程联动唯一的人机拒绝点：页面调用方拿到 false 只会静默收手，
            // 所以提示必须在这里发，否则用户点了按钮毫无反应。
            ClassTimeDrawRestrictionNotice.Show(linkageService.Snapshot);
            return false;
        }

        return await securityService.AuthorizeAsync(
            [.. operations, SecurityOperation.BypassClassTimeRestriction],
            action,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     在**不触发任何交互**的前提下判定一次抽取能否执行。
    /// </summary>
    /// <remarks>
    ///     集控节点收到远程抽取命令时先问这里：<see cref="LinkageDrawGate.RequiresLocalVerification" />
    ///     意味着真正执行会弹出密码框，而教室机可能没人，命令会一直挂着——远程路径直接拒绝，
    ///     让控制台看到"设备拒绝"，而不是让设备卡住。判定通过后仍要调用
    ///     <see cref="AuthorizeAsync" />（抽取的唯一一道门），那时它不会再要求验证。
    /// </remarks>
    public LinkageDrawGate EvaluateGate(SecurityOperation operation) => EvaluateGate([operation]);

    public LinkageDrawGate EvaluateGate(IReadOnlyCollection<SecurityOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        if (controlDrawGate.IsDrawLocked)
            return LinkageDrawGate.BlockedByRemoteLock;

        if (!IsLinkageRestricted)
            return operations.Any(securityService.RequiresVerification)
                ? LinkageDrawGate.RequiresLocalVerification
                : LinkageDrawGate.Allowed;

        if (!linkageService.Settings.VerificationRequired)
            return LinkageDrawGate.BlockedByClassTime;

        // 联动限制后的抽取要额外过 BypassClassTimeRestriction，而它永远需要当场验证。
        // 户外课程限制沿用 BlockedByClassTime：它本就发生在上课时段，远程控制台的
        // "上课时间"文案仍然准确。
        return LinkageDrawGate.RequiresLocalVerification;
    }

    /// <summary>
    ///     联动闸门是否在当前时刻限制抽取：非上课时段限制与上课时段户外课程限制共用同一套
    ///     验证例外（<see cref="SecurityOperation.BypassClassTimeRestriction" />）。
    /// </summary>
    private bool IsLinkageRestricted => linkageService.IsClassTimeRestrictionActive
        || linkageService.IsConfirmedOutdoorCourse;

    public string GetCourseName() => linkageService.GetSubjectFilter();
}
