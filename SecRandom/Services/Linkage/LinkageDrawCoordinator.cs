using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Enums.Configs;
using SecRandom.Services.Security;

namespace SecRandom.Services.Linkage;

public sealed class LinkageDrawCoordinator(
    CourseLinkageService linkageService,
    ISecurityService securityService)
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
        if (!linkageService.IsConfirmedNonClassTime)
            return await securityService.AuthorizeAsync(operations, action, cancellationToken).ConfigureAwait(false);

        if (!linkageService.Settings.VerificationRequired)
            return false;

        return await securityService.AuthorizeAsync(
            [.. operations, SecurityOperation.BypassClassTimeRestriction],
            action,
            cancellationToken).ConfigureAwait(false);
    }

    public string GetCourseName() => linkageService.GetSubjectFilter();
}
