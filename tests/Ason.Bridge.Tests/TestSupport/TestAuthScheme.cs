using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// A minimal authentication scheme for the tests: a request is authenticated when it carries
/// <see cref="Header"/> with <see cref="Value"/>. Real applications plug their own scheme (JWT, cookies, a
/// gateway-issued header); the bridge only ever names an authorization policy, so a test scheme is enough to
/// prove the policy is enforced on every adapter.
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions> {

    public const string Scheme = "ason-test";
    public const string Header = "X-Ason-Test-Key";
    public const string Value = "let-me-in";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (!Request.Headers.TryGetValue(Header, out var provided) || provided != Value) {
            return Task.FromResult(AuthenticateResult.Fail($"'{Header}' is missing or wrong."));
        }
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "ason-test-caller") }, Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

internal static class TestAuth {

    /// <summary>The authorization policy name the test hosts require.</summary>
    public const string Policy = "ason-bridge-caller";

    /// <summary>Registers the scheme and a policy that requires an authenticated caller.</summary>
    public static IServiceCollection AddTestAuth(this IServiceCollection services) {
        services.AddAuthentication(TestAuthHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, null);
        services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy.RequireAuthenticatedUser()));
        return services;
    }

    /// <summary>The header a caller must send for <see cref="TestAuthHandler"/> to accept it.</summary>
    public static IReadOnlyDictionary<string, string> Authorized => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        [TestAuthHandler.Header] = TestAuthHandler.Value
    };
}
