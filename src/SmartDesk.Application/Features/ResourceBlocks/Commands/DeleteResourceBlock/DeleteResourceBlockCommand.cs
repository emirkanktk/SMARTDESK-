using MediatR;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.DeleteResourceBlock;

// Admin only. Removing a block only frees time, so it needs no booking lock.
public sealed record DeleteResourceBlockCommand(Guid Id) : IRequest;
