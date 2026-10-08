using MediatR;
using SmartDesk.Application.Features.ResourceBlocks.Common;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;

// Admin, or a Manager for their team's own resource (enforced on the endpoint and in the
// handler). Reason is shown to anyone whose booking the block refuses, so it should say why
// ("Projector replacement"), not just "blocked".
//
// CancelReservations (ADR-0038): the Confirmed bookings in the window are cancelled with the
// block, and everyone on them is told why. Without it, they're a 409 to deal with first.
public sealed record CreateResourceBlockCommand(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string Reason,
    bool CancelReservations = false) : IRequest<ResourceBlockDto>;
