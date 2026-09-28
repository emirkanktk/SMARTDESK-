using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.DeleteResourceBlock;

public sealed class DeleteResourceBlockCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<DeleteResourceBlockCommand>
{
    public async Task Handle(DeleteResourceBlockCommand request, CancellationToken cancellationToken)
    {
        var block = await db.ResourceBlocks
            .OnUsableResources(currentUser)
            .Include(b => b.Resource).ThenInclude(r => r.Teams)
            .SingleOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ResourceBlock), request.Id);
        ResourceManagement.RequireManageable(block.Resource, currentUser);

        db.ResourceBlocks.Remove(block);
        await db.SaveChangesAsync(cancellationToken);
    }
}
