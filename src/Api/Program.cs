using System.Security.Claims;
using Api;
using Application;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
builder.Services.AddPedidos(builder.Configuration).AddProcessamento(builder.Configuration)
    .AddSeguranca().AddNotificacoes();
var compilada = builder.Configuration.GetValue("Diagnostico:ConsultaCompilada", false);
var revisao = builder.Configuration["Revisao"] ?? "local";
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<PedidosDb>().Database.MigrateAsync();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapGet("/health", async (PedidosDb db, CancellationToken token) =>
    await db.Database.CanConnectAsync(token)
        ? Results.Ok(new { status = "Healthy", revisao })
        : Results.Json(new { status = "Unhealthy", revisao }, statusCode: 503));

app.MapPost("/pedidos", async (CriarPedidoRequest body, ClaimsPrincipal user, Catalogo catalogo,
    PedidosStore store, CancellationToken token) =>
{
    try
    {
        var pedido = Contratos.Mapear(body);
        var resposta = catalogo.Preparar(pedido);
        await store.Criar(pedido, resposta, Dono(user), token);
        return Results.Created($"/pedidos/{resposta.Id}", resposta);
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { erro = e.Message });
    }
}).RequireAuthorization();

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
}).RequireAuthorization();

app.MapConsultas(compilada);
app.MapGet("/pedidos", async (string? categoria, int? pagina, int? tamanho, ClaimsPrincipal user,
    PedidosStore store, CancellationToken token) =>
{
    if ((pagina ?? 1) is < 1 or > 10_000 || (tamanho ?? 20) is < 1 or > 100)
        return Results.BadRequest();
    return Results.Ok(await store.Listar(Dono(user), categoria, pagina ?? 1, tamanho ?? 20, token));
}).RequireAuthorization();

app.MapPost("/pedidos/{id:guid}/processamento", async (Guid id, ClaimsPrincipal user,
    PedidosStore store, Fila fila, CancellationToken token) =>
{
    var dono = Dono(user);
    var pedido = await store.Buscar(id, dono, token);
    if (pedido is null)
        return Results.NotFound();
    if (pedido.Estado == "processado")
        return Results.Conflict();
    return fila.Tentar(new(id, dono), token) switch
    {
        Inclusao.Aceito => Results.Accepted($"/pedidos/{id}"),
        Inclusao.Duplicado => Results.Conflict(),
        _ => Results.StatusCode(503)
    };
}).RequireAuthorization();
app.MapGet("/metricas/fila", (Fila fila) => fila.Medidas()).RequireAuthorization();

app.MapGet("/indicadores", async (string? categoria, PedidosDb db, CancellationToken token) =>
{
    if (categoria is not (null or "normal" or "regional" or "expresso"))
        return Results.BadRequest();
    var processados = await db.Pedidos.CountAsync(p => p.Estado == "processado" &&
        (categoria == null || p.Categoria == categoria), token);
    return Results.Ok(new { processados, amostra = Guid.NewGuid() });
}).CacheOutput("indicadores");

app.MapHub<PedidosHub>("/hubs/pedidos");
app.Run();

static string Dono(ClaimsPrincipal usuario) => usuario.FindFirstValue(ClaimTypes.NameIdentifier)!;

public partial class Program;
