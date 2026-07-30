using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using EmailService.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EmailService.Authentication;

public class ApiKeySchemeOptions : AuthenticationSchemeOptions;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeySchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptionsMonitor<ApiKeyOptions> apiKeyOptions)
    : AuthenticationHandler<ApiKeySchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = apiKeyOptions.CurrentValue;

        if (!options.Enabled)
        {
            return Task.FromResult(AuthenticateResult.Success(TicketFor("anonymous", isAdmin: true)));
        }

        if (!Request.Headers.TryGetValue(options.HeaderName, out var provided) || provided.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API key."));
        }

        var presented = provided.ToString();

        foreach (var (name, entry) in options.Keys)
        {
            if (FixedTimeEquals(entry.Key, presented))
            {
                return Task.FromResult(AuthenticateResult.Success(TicketFor(name, entry.IsAdmin)));
            }
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }

    private static bool FixedTimeEquals(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented));

    private static AuthenticationTicket TicketFor(string name, bool isAdmin)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };

        if (isAdmin)
        {
            claims.Add(new Claim("scope", "admin"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
    }
}
