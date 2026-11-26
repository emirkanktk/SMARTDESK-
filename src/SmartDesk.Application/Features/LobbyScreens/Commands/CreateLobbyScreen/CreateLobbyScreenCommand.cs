using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.LobbyScreens.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.LobbyScreens.Commands.CreateLobbyScreen;

// Sets up a lobby screen (POST /api/lobby-screens, Admin, ADR-0064): Name is what the Admins call
// it, Location the floor it shows - one that has resources - or null for the whole office. The
// answer has the link to open on the screen, shown this once.
public sealed record CreateLobbyScreenCommand(string Name, string? Location) : IRequest<NewLobbyScreenDto>;

public sealed class CreateLobbyScreenCommandValidator : AbstractValidator<CreateLobbyScreenCommand>
{
    public CreateLobbyScreenCommandValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(LobbyScreenRules.MaxNameLength)
            .Must(name => !name.Any(char.IsControl)).WithMessage(_ => Messages.Validation_NoControlCharacters);
        RuleFor(x => x.Location).MaximumLength(200);
    }
}

public sealed class CreateLobbyScreenCommandHandler(IApplicationDbContext db, IFrontendLinks links, TimeProvider timeProvider)
    : IRequestHandler<CreateLobbyScreenCommand, NewLobbyScreenDto>
{
    public async Task<NewLobbyScreenDto> Handle(CreateLobbyScreenCommand request, CancellationToken cancellationToken)
    {
        var location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
        if (location is not null && !await db.Resources.AnyAsync(r => r.Location == location, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Location), Messages.LobbyScreen_UnknownFloor)]);
        }

        if (await db.LobbyScreens.CountAsync(cancellationToken) >= LobbyScreenRules.MaxScreens)
        {
            throw new ConflictException(string.Format(Messages.LobbyScreen_Full, LobbyScreenRules.MaxScreens))
            {
                Code = ConflictCodes.LobbyScreensFull,
            };
        }

        var token = LobbyScreenRules.NewToken();
        var screen = new LobbyScreen
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            Location = location,
            TokenHash = LobbyScreenRules.HashToken(token),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.LobbyScreens.Add(screen);
        await db.SaveChangesAsync(cancellationToken);

        return new NewLobbyScreenDto(LobbyScreenDto.From(screen), links.LobbyScreen(token));
    }
}
