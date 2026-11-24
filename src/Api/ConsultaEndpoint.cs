using Application;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Infrastructure;

namespace Api;

public static class ConsultaEndpoint
{
    public static void MapConsultas(this IEndpointRouteBuilder app, bool compilada)
    {
        app.MapGet("/pedidos/{id:guid}", (Guid id, PedidosStore store, HttpContext http, CancellationToken token) =>
            Buscar(id, "v1", store, http, compilada, token)).RequireAuthorization();
        app.MapGet("/api/{versao}/pedidos/{id:guid}", (Guid id, string versao, PedidosStore store, HttpContext http, CancellationToken token) =>
            Buscar(id, versao, store, http, compilada, token)).RequireAuthorization();
    }

    private static async Task<IResult> Buscar(Guid id, string versao, PedidosStore store, HttpContext http, bool compilada, CancellationToken token)
    {
        if (versao is not ("v1" or "v2"))
            return Results.BadRequest(new { erro = "Use v1 ou v2." });
        var dono = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pedido = compilada ? await store.BuscarCompilado(id, dono) : await store.Buscar(id, dono, token);
        if (pedido is null)
            return Results.NotFound();
        object dto = versao == "v1"
            ? new PedidoResponse(pedido.Id, pedido.TotalCentavos, pedido.Categoria)
            : new PedidoV2(pedido.Id, pedido.TotalCentavos / 100m, "BRL", pedido.Categoria, pedido.Estado);
        var body = JsonSerializer.SerializeToUtf8Bytes(dto, JsonSerializerOptions.Web);
        var etag = new EntityTagHeaderValue("\"" + Convert.ToHexString(SHA256.HashData(body)) + "\"");
        http.Response.GetTypedHeaders().ETag = etag;
        http.Response.Headers.CacheControl = "private, no-cache";
        if (EntityTagHeaderValue.TryParseStrictList([http.Request.Headers.IfNoneMatch.ToString()], out var tags)
            && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false)))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Bytes(body, "application/json; charset=utf-8");
    }
}
