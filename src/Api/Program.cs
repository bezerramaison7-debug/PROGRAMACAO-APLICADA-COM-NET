using Api;
using Application;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
builder.Services.AddPedidos(builder.Configuration)
    .AddProcessamento(builder.Configuration).AddNotificacoes();
var compilada = builder.Configuration.GetValue("Diagnostico:ConsultaCompilada", false);
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<PedidosDb>().Database.MigrateAsync();
app.MapGet("/", () => "Hub de Processamento de Pedidos");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapPost("/pedidos", async (CriarPedidoRequest body, Catalogo catalogo, PedidosStore store, CancellationToken token) =>
{
    try
    {
        var pedido = Contratos.Mapear(body);
        var resposta = catalogo.Preparar(pedido);
        await store.Criar(pedido, resposta, "laboratorio", token);
        return Results.Created($"/pedidos/{resposta.Id}", resposta);
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { erro = e.Message });
    }
});
app.MapPost("/lotes/validar", async (HttpRequest request, CancellationToken token) =>
{
    try
    {
        var itens = await Importacao.LerAsync(request.Body, token);
        return Results.Ok(new { itens = itens.Count });
    }
    catch (FormatException e)
    {
        return Results.BadRequest(new { erro = e.Message });
    }
});
app.MapConsultas(compilada);
app.MapGet("/pedidos", async (string? categoria, int? pagina, int? tamanho, PedidosStore store, CancellationToken token) =>
{
    if ((pagina ?? 1) is < 1 or > 10_000 || (tamanho ?? 20) is < 1 or > 100)
        return Results.BadRequest();
    return Results.Ok(await store.Listar("laboratorio", categoria, pagina ?? 1, tamanho ?? 20, token));
});
app.MapPost("/pedidos/{id:guid}/processamento", async (Guid id, PedidosStore store, Fila fila, CancellationToken token) =>
{
    var pedido = await store.Buscar(id, "laboratorio", token);
    if (pedido is null)
        return Results.NotFound();
    if (pedido.Estado == "processado")
        return Results.Conflict();
    return fila.Tentar(new(id, "laboratorio"), token) switch
    {
        Inclusao.Aceito => Results.Accepted($"/pedidos/{id}"),
        Inclusao.Duplicado => Results.Conflict(),
        _ => Results.StatusCode(503)
    };
});
app.MapGet("/metricas/fila", (Fila fila) => fila.Medidas());
app.Run();

public partial class Program;
