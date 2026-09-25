using MediatR;
using SmartDesk.Application.Features.ResourceBlocks.Common;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;

// Admin only (enforced on the endpoint). Reason is shown to anyone whose booking the block
// refuses, so it should say why ("Projector replacement"), not just "blocked".
public sealed record CreateResourceBlockCommand(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string Reason) : IRequest<ResourceBlockDto>;
