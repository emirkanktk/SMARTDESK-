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

    public Task RememberLanguageAsync(Guid userId, string language, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RenameAsync(Guid userId, string firstName, string lastName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PasswordResetToken?> CreatePasswordResetTokenAsync(string email, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ResetPasswordResult> ResetPasswordAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ChangePasswordResult> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
