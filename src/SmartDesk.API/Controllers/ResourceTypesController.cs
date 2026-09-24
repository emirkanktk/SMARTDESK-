using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.ResourceTypes.Queries.GetResourceTypes;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/resource-types")]
[Authorize]
public sealed class ResourceTypesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ResourceTypeDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ResourceTypeDto>> GetAll(CancellationToken cancellationToken) =>
        sender.Send(new GetResourceTypesQuery(), cancellationToken);
}
