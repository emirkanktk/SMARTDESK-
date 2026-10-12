using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Commands.SetResourcePosition;

// Places a resource on its floor's plan, or takes it off with neither x nor y (PUT
// /api/resources/{id}/position): fractions of the plan's width and height. Whoever may edit the
// resource may move it (ADR-0022, ADR-0041).
public sealed record SetResourcePositionCommand(double? X, double? Y) : IRequest<ResourceDto>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}

public sealed class SetResourcePositionCommandValidator : AbstractValidator<SetResourcePositionCommand>
{
    public SetResourcePositionCommandValidator()
    {
        RuleFor(x => x)
            .Must(p => p.X is null && p.Y is null || p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1)
            .WithMessage(_ => Messages.Resources_PositionInvalid)
            .OverridePropertyName("Position");
    }
}

public sealed class SetResourcePositionCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ResourceReader reader) : IRequestHandler<SetResourcePositionCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(SetResourcePositionCommand request, CancellationToken cancellationToken)
    {
        var resource = await db.Resources
            .UsableBy(currentUser)
            .Include(r => r.Teams)
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), request.Id);
        ResourceManagement.RequireManageable(resource, currentUser);

        resource.MapX = request.X;
        resource.MapY = request.Y;
        await db.SaveChangesAsync(cancellationToken);

        return (await reader.FindAsync(db.Resources.AsNoTracking().Where(r => r.Id == resource.Id), cancellationToken))!;
    }
}
