using MediatR;

namespace SmartDesk.Application.Features.CheckIn.Commands.ReleaseNoShows;

// Releases the bookings nobody checked in to in time (ADR-0030), run every few seconds by
// NoShowReleaseWorker. Returns how many were released.
public sealed record ReleaseNoShowsCommand : IRequest<int>;
