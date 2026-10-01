using Factum.Backend.Common;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

public interface IAuthProvider
{
    string Mode { get; }
    Task<Result<User>> AuthenticateAsync(string dni, string username, string password,
        CancellationToken ct = default);
}
