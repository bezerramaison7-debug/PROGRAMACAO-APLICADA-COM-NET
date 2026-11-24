using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Application;
using Xunit.Abstractions;

namespace Integration;

public sealed class ContratosTests(ITestOutputHelper output)
{
    internal static readonly CriarPedidoRequest Payload = new("SE", false, [new("LAB", 2, 150)]);

    internal static async Task<PedidoResponse> Criar(HttpClient cliente)
    {
        using var response = await cliente.PostAsJsonAsync("/pedidos", Payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PedidoResponse>())!;
    }

    [Fact]
    public async Task PersisteV1EProtegeIsolamento()
    {
        await using var fabrica = new HubFactory();
        using var alice = fabrica.Cliente();
        using var bob = fabrica.Cliente("fixture-bob");
        using var anonimo = fabrica.Cliente(null);
        Assert.Empty((await alice.GetFromJsonAsync<Pagina>("/pedidos"))!.Pedidos);
        var pedido = await Criar(alice);
        Assert.Equal(pedido, await alice.GetFromJsonAsync<PedidoResponse>($"/pedidos/{pedido.Id}"));
        Assert.Equal(pedido, await alice.GetFromJsonAsync<PedidoResponse>($"/api/v1/pedidos/{pedido.Id}"));
        var v2 = await alice.GetFromJsonAsync<PedidoV2>($"/api/v2/pedidos/{pedido.Id}");
        Assert.Equal((3m, "BRL", "recebido"), (v2!.Total, v2.Moeda, v2.Estado));
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync($"/api/v3/pedidos/{pedido.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/pedidos/{pedido.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/pedidos/{pedido.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/pedidos", Payload with { Regiao = "XX" })).StatusCode);
    }

    [Fact]
    public async Task EtagEstavelEAtualizacaoInvalidamCachePublico()
    {
        await using var fabrica = new HubFactory();
        using var operador = fabrica.Cliente();
        using var publico = fabrica.Cliente(null);
        var pedido = await Criar(operador);
        var uri = $"/api/v2/pedidos/{pedido.Id}";

        var inicial = await operador.GetAsync(uri);
        var tag = inicial.Headers.ETag;
        Assert.NotNull(tag);
        Assert.Equal(tag, (await operador.GetAsync(uri)).Headers.ETag);
        using var condicional = new HttpRequestMessage(HttpMethod.Get, uri);
        condicional.Headers.TryAddWithoutValidation("If-None-Match", $"\"outro\", W/{tag}");
        using var igual = await operador.SendAsync(condicional);
        Assert.Equal(HttpStatusCode.NotModified, igual.StatusCode);
        Assert.Empty(await igual.Content.ReadAsByteArrayAsync());
        using var invalido = new HttpRequestMessage(HttpMethod.Get, uri);
        invalido.Headers.TryAddWithoutValidation("If-None-Match", "invalido");
        Assert.Equal(HttpStatusCode.OK, (await operador.SendAsync(invalido)).StatusCode);

        const string indicadores = "/indicadores?categoria=normal";
        var marca = Stopwatch.GetTimestamp();
        var primeiro = await publico.GetFromJsonAsync<Indicador>(indicadores);
        var latenciaInicial = Stopwatch.GetElapsedTime(marca).TotalMilliseconds;
        var latencias = new double[20];
        Indicador? segundo = null;
        for (var i = 0; i < latencias.Length; i++)
        {
            marca = Stopwatch.GetTimestamp();
            segundo = await publico.GetFromJsonAsync<Indicador>(indicadores);
            latencias[i] = Stopwatch.GetElapsedTime(marca).TotalMilliseconds;
            Assert.Equal(primeiro, segundo);
        }
        Array.Sort(latencias);
        output.WriteLine($"Output cache: primeira consulta {latenciaInicial:F3} ms; 20 hits, p50={latencias[9]:F3} ms.");
        Assert.Equal(primeiro, segundo);
        Assert.Equal(0, primeiro!.Processados);
        Assert.NotEqual(primeiro.Amostra, (await publico.GetFromJsonAsync<Indicador>("/indicadores?categoria=regional"))!.Amostra);

        Assert.Equal(HttpStatusCode.Accepted, (await operador.PostAsync($"/pedidos/{pedido.Id}/processamento", null)).StatusCode);
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await publico.GetFromJsonAsync<Indicador>(indicadores, prazo.Token))!.Processados != 1)
            await Task.Delay(10, prazo.Token);
        using var antiga = new HttpRequestMessage(HttpMethod.Get, uri);
        antiga.Headers.IfNoneMatch.Add(tag);
        using var atual = await operador.SendAsync(antiga);
        Assert.Equal(HttpStatusCode.OK, atual.StatusCode);
        Assert.NotEqual(tag, atual.Headers.ETag);
        Assert.Equal("processado", (await atual.Content.ReadFromJsonAsync<PedidoV2>())!.Estado);
        var indicador = await publico.GetFromJsonAsync<Indicador>(indicadores);
        Assert.NotEqual(primeiro.Amostra, indicador!.Amostra);
        Assert.Equal(HttpStatusCode.Conflict, (await operador.PostAsync($"/pedidos/{pedido.Id}/processamento", null)).StatusCode);
    }

    public sealed record Indicador(int Processados, Guid Amostra);
}
