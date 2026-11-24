using Application;
using Infrastructure;

namespace Api;

public static class ConsultaEndpoint
{
    public static void MapConsultas(this IEndpointRouteBuilder app, bool compilada)
    {
        app.MapGet("/pedidos/{id:guid}", (Guid id, PedidosStore store, CancellationToken token) =>
            Buscar(id, "v1", store, compilada, token));
        app.MapGet("/api/{versao}/pedidos/{id:guid}", (Guid id, string versao, PedidosStore store, CancellationToken token) =>
            Buscar(id, versao, store, compilada, token));
    }

    private static async Task<IResult> Buscar(Guid id, string versao, PedidosStore store, bool compilada, CancellationToken token)
    {
        if (versao is not ("v1" or "v2"))
            return Results.BadRequest(new { erro = "Use v1 ou v2." });
        var pedido = compilada ? await store.BuscarCompilado(id, "laboratorio") : await store.Buscar(id, "laboratorio", token);
        if (pedido is null)
            return Results.NotFound();
        return versao == "v1"
            ? Results.Ok(new PedidoResponse(pedido.Id, pedido.TotalCentavos, pedido.Categoria))
            : Results.Ok(new PedidoV2(pedido.Id, pedido.TotalCentavos / 100m, "BRL", pedido.Categoria, pedido.Estado));
    }
}
