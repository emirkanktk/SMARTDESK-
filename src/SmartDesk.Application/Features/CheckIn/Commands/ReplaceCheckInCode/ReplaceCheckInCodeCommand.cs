using MediatR;
using SmartDesk.Application.Features.CheckIn.Common;

namespace SmartDesk.Application.Features.CheckIn.Commands.ReplaceCheckInCode;

// A new secret for the resource's QR code (ADR-0030), e.g. when the printed one has been
// photographed and passed around. Codes already printed stop working at once.
public sealed record ReplaceCheckInCodeCommand(Guid ResourceId) : IRequest<CheckInCodeDto>;
