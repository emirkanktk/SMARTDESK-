using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Issues.Common;

// Turns reports into IssueReportDtos with whoever reported them named, in one lookup for the
// whole list.
public sealed class IssueReader(IIdentityService identityService)
{
    // Keeps the order of `issues`.
    public async Task<IReadOnlyList<IssueReportDto>> ListAsync(IQueryable<IssueReport> issues, CancellationToken cancellationToken)
    {
        var dtos = await issues.Select(IssueReportDto.Projection).ToListAsync(cancellationToken);
        if (dtos.Count == 0)
        {
            return dtos;
        }

        // Reported at the door without an account: nobody to name (ADR-0050).
        var names = await identityService.GetDisplayNamesAsync(
            dtos.Where(d => d.ReportedByUserId is not null).Select(d => d.ReportedByUserId!.Value).Distinct().ToList(),
            cancellationToken);
        return dtos
            .Select(d => d with
            {
                ReportedByName = d.ReportedByUserId is { } reporter ? names.GetValueOrDefault(reporter) : null,
            })
            .ToList();
    }

    public async Task<IssueReportDto> GetAsync(IQueryable<IssueReport> issues, CancellationToken cancellationToken) =>
        (await ListAsync(issues, cancellationToken)).Single();
}
