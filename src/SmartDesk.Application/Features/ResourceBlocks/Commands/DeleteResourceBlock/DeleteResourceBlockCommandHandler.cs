using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.DeleteResourceBlock;

public sealed class DeleteResourceBlockCommandHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteResourceBlockCommand>
{
    public async Task Handle(DeleteResourceBlockCommand request, CancellationToken cancellationToken)
    {
        var block = await db.ResourceBlocks.SingleOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ResourceBlock), request.Id);

        db.ResourceBlocks.Remove(block);
        await db.SaveChangesAsync(cancellationToken);
    }
}
