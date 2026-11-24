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
app.Run();

public partial class Program;
