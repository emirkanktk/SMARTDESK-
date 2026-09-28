using MediatR;

namespace SmartDesk.Application.Features.Reservations.Commands.SendDueReminders;

// Run by the reminder job every few seconds (ADR-0018). Sends each reminder whose moment has
// come and whose reservation hasn't started yet, once. Returns how many went out.
public sealed record SendDueRemindersCommand : IRequest<int>;
