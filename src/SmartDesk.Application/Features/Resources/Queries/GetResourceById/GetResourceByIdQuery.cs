using MediatR;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Queries.GetResourceById;

public sealed record GetResourceByIdQuery(Guid Id) : IRequest<ResourceDto>;
