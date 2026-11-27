using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Announcements.Commands.DeleteAnnouncement;

// Takes an announcement down (DELETE /api/announcements/{id}, Admin, ADR-0065), showing or ended:
// it leaves every dashboard and lobby screen on their next look. The history keeps that it was.
public sealed record DeleteAnnouncementCommand(Guid Id) : IRequest;

public sealed class DeleteAnnouncementCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteAnnouncementCommand>
{
    public async Task Handle(DeleteAnnouncementCommand request, CancellationToken cancellationToken)
    {
        var announcement = await db.Announcements.SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Announcement), request.Id);
        db.Announcements.Remove(announcement);
        await db.SaveChangesAsync(cancellationToken);
    }
}
