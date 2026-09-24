using FluentAssertions;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Queries.GetCurrentUser;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Tests.Auth;

public class GetCurrentUserQueryHandlerTests
{
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IIdentityService> _identityService = new();

    private GetCurrentUserQueryHandler CreateHandler() => new(_currentUser.Object, _identityService.Object);

    [Fact]
    public async Task Handle_AuthenticatedUser_ReturnsTheirProfile()
    {
        var userId = Guid.NewGuid();
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _identityService
            .Setup(s => s.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfile(userId, "System", "Admin", "admin@smartdesk.local", [Roles.Admin]));

        var dto = await CreateHandler().Handle(new GetCurrentUserQuery(), CancellationToken.None);

        dto.Should().BeEquivalentTo(new CurrentUserDto(userId, "System", "Admin", "admin@smartdesk.local", [Roles.Admin]));
    }

    [Fact]
    public async Task Handle_NoUserIdOnRequest_ThrowsUnauthorized()
    {
        _currentUser.Setup(c => c.UserId).Returns((Guid?)null);

        var act = () => CreateHandler().Handle(new GetCurrentUserQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Handle_TokenForDeletedUser_ThrowsUnauthorized()
    {
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _identityService
            .Setup(s => s.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var act = () => CreateHandler().Handle(new GetCurrentUserQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }
}
