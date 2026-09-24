using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Tests.TestSupport;

// Only the display-name lookup is backed; the auth methods aren't used by reservation handlers.
public sealed class FakeIdentityService(IReadOnlyDictionary<Guid, string>? names = null) : IIdentityService
{
    private readonly IReadOnlyDictionary<Guid, string> _names = names ?? new Dictionary<Guid, string>();

    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            userIds.Where(_names.ContainsKey).ToDictionary(id => id, id => _names[id]));

    public Task<UserProfile?> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<UserProfile?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
