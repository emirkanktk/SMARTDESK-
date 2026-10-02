using MediatR;
using SmartDesk.Application.Features.CheckIn.Common;

namespace SmartDesk.Application.Features.CheckIn.Queries.GetCheckInCode;

// The QR code to print for a resource (ADR-0030). Every resource has one, whether or not
// check-in is on, so it can be put up before it's switched on.
public sealed record GetCheckInCodeQuery(Guid ResourceId) : IRequest<CheckInCodeDto>;
