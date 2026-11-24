using Application;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PedidosDb>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Pedidos") ?? "Data Source=pedidos.db"));
builder.Services.AddScoped<PedidosStore>();
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<PedidosDb>().Database.MigrateAsync();
var catalogo = new Catalogo();
app.MapGet("/", () => "Hub de Processamento de Pedidos");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapPost("/pedidos", async (CriarPedidoRequest body, PedidosStore store, CancellationToken token) =>
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
app.MapGet("/pedidos/{id:guid}", async (Guid id, PedidosStore store, CancellationToken token) =>
{
    var pedido = await store.Buscar(id, "laboratorio", token);
    return pedido is null ? Results.NotFound() :
        Results.Ok(new PedidoResponse(pedido.Id, pedido.TotalCentavos, pedido.Categoria));
});
app.MapGet("/pedidos", async (string? categoria, int? pagina, int? tamanho, PedidosStore store, CancellationToken token) =>
{
    if ((pagina ?? 1) is < 1 or > 10_000 || (tamanho ?? 20) is < 1 or > 100)
        return Results.BadRequest();
    return Results.Ok(await store.Listar("laboratorio", categoria, pagina ?? 1, tamanho ?? 20, token));
});
app.Run();

public partial class Program;
