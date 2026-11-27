using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Announcements.Common;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Announcements.Commands.CreateAnnouncement;

// Posts an announcement (POST /api/announcements, Admin, ADR-0065): it shows from now until
// EndsAt, at most AnnouncementRules.MaxDays on. Notify also sends it to the phones and browsers
// that have notifications on (ADR-0056), but its author's.
public sealed record CreateAnnouncementCommand(string Title, string? Body, AnnouncementTone Tone, DateTime EndsAt, bool Notify)
    : IRequest<AnnouncementDto>;

public sealed class CreateAnnouncementCommandValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementCommandValidator()
    {
        RuleFor(x => x.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(AnnouncementRules.MaxTitleLength)
            .Must(title => !title.Any(char.IsControl)).WithMessage(_ => Messages.Validation_NoControlCharacters);
        // Lines are kept: a few of them make a short note.
        RuleFor(x => x.Body)
            .MaximumLength(AnnouncementRules.MaxBodyLength)
            .Must(body => body is null || !body.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            .WithMessage(_ => Messages.Validation_NoControlCharacters);
        RuleFor(x => x.Tone).IsInEnum();
    }
}

public sealed class CreateAnnouncementCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IPushSender push,
    TimeProvider timeProvider) : IRequestHandler<CreateAnnouncementCommand, AnnouncementDto>
{
    public async Task<AnnouncementDto> Handle(CreateAnnouncementCommand request, CancellationToken cancellationToken)
    {
        var author = currentUser.UserId ?? throw new UnauthorizedException();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var endsAt = request.EndsAt.Kind == DateTimeKind.Local
            ? request.EndsAt.ToUniversalTime()
            : DateTime.SpecifyKind(request.EndsAt, DateTimeKind.Utc);
        if (endsAt <= now)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.EndsAt), Messages.Announcement_EndsInPast)]);
        }

        if (endsAt > now.AddDays(AnnouncementRules.MaxDays))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.EndsAt), string.Format(Messages.Announcement_TooLong, AnnouncementRules.MaxDays))]);
        }

        var announcement = new Announcement
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title.Trim(),
            Body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim(),
            Tone = request.Tone,
            CreatedAt = now,
            EndsAt = endsAt,
            CreatedByUserId = author,
        };
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(cancellationToken);
        var posted = (await AnnouncementDto.WithAuthorsAsync([AnnouncementDto.From(announcement)], identityService, cancellationToken))[0];

        if (request.Notify)
        {
            // Everyone with notifications on, the Admin who just posted it aside.
            var recipients = await db.PushSubscriptions
                .Where(s => s.UserId != author)
                .Select(s => s.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (recipients.Count > 0)
            {
                push.Queue(new PushNotice(PushTopic.Announcement, recipients) { Announcement = posted });
            }
        }

        return posted;
    }
}
