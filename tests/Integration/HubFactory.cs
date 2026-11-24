using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Integration;

internal sealed class HubFactory(Action<IServiceCollection>? configurar = null,
    IReadOnlyDictionary<string, string?>? parametros = null) : WebApplicationFactory<Program>
{
    private readonly string connectionString = $"Data Source=integ-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
    private SqliteConnection? anchor;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        anchor = new SqliteConnection(connectionString);
        anchor.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pedidos"] = connectionString,
                ["Auth:Operadores:alice"] = "fixture-alice",
                ["Auth:Operadores:bob"] = "fixture-bob",
                ["Logging:LogLevel:Default"] = "Warning"
            });
            if (parametros is not null)
                config.AddInMemoryCollection(parametros);
        });
        if (configurar is not null)
            builder.ConfigureServices(configurar);
    }

    internal HttpClient Cliente(string? chave = "fixture-alice")
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        if (chave is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", chave);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (anchor is not null)
            await anchor.DisposeAsync();
    }
}
