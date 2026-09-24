using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.CreateResource;

public sealed class CreateResourceCommandValidator(IApplicationDbContext db)
    : ResourceFieldsValidator<CreateResourceCommand>(db);
