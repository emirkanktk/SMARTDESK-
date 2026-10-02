using MediatR;

namespace SmartDesk.Application.Features.Reservations.Commands.SendEndingSoonNotices;

// Asks the owner of each booking that's under way and ends within ExtensionRules.NoticeBefore
// whether they need longer (ADR-0033), once per end. Returns how many were asked; run by a
// background job.
public sealed record SendEndingSoonNoticesCommand : IRequest<int>;
