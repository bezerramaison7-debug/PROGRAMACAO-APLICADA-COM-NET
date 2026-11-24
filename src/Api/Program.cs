using Application;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var catalogo = new Catalogo();
app.MapGet("/", () => "Hub de Processamento de Pedidos");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapPost("/pedidos", (CriarPedidoRequest body) =>
{
    try
    {
        return Results.Ok(catalogo.Preparar(Contratos.Mapear(body)));
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
app.Run();

public partial class Program;
