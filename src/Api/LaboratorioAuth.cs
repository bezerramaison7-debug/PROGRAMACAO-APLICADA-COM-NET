using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Api;

public sealed class Operadores(IConfiguration config)
{
    public string? Identificar(string token)
    {
        if (token.Length is < 8 or > 256)
            return null;
        var digest = Hash(token);
        var correspondencias = config.GetSection("Auth:Operadores").GetChildren()
            .Where(p => p.Value is { Length: >= 8 } valor &&
                CryptographicOperations.FixedTimeEquals(digest, Hash(valor))).ToArray();
        return correspondencias.Length == 1 ? correspondencias[0].Key : null;
    }

    public bool Ativo(string usuario, string hash)
    {
        var atual = config[$"Auth:Operadores:{usuario}"];
        return atual is { Length: >= 8 } && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(hash), Hash(atual));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

public sealed class LaboratorioAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder, Operadores operadores)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : "";
        if (token.Length == 0 && Request.Path.StartsWithSegments("/hubs/pedidos"))
            token = Request.Query["access_token"].ToString();
        if (token.Length == 0)
            return Task.FromResult(AuthenticateResult.NoResult());
        var usuario = operadores.Identificar(token);
        if (usuario is null)
            return Task.FromResult(AuthenticateResult.Fail("Credencial inválida."));
        var claims = new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, usuario),
            new("credencial", Convert.ToHexString(Operadores.Hash(token)))
        ], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new(new(claims), Scheme.Name)));
    }
}
