using System.Collections.Immutable;
using Domain;

namespace Application;

public sealed record ItemRequest(string Sku, int Quantidade, long PrecoCentavos);
public sealed record CriarPedidoRequest(string Regiao, bool Prioritario, ImmutableArray<ItemRequest> Itens);
public sealed record PedidoResponse(Guid Id, long TotalCentavos, string Categoria);
public sealed record PedidoV2(Guid Id, decimal Total, string Moeda, string Categoria, string Estado);
public sealed record PedidoResumo(Guid Id, long TotalCentavos, string Categoria, string Estado);
public sealed record Pagina(int Total, IReadOnlyList<PedidoResumo> Pedidos);

public static class Contratos
{
    public static Pedido Mapear(CriarPedidoRequest request)
    {
        if (request.Itens.IsDefaultOrEmpty || request.Itens.Length > 100 || request.Itens.Any(i => i is null))
            throw new ArgumentException("Informe de 1 a 100 itens válidos.");
        if (request.Regiao is not ("SE" or "S" or "N" or "NE" or "CO"))
            throw new ArgumentException("Região inválida.");
        var itens = request.Itens.Select(i => new ItemPedido(i.Sku, i.Quantidade, i.PrecoCentavos));
        return new(new(Guid.NewGuid()), request.Regiao, request.Prioritario, [.. itens]);
    }

    public static string Classificar(Pedido pedido) => pedido switch
    {
        { Itens.IsDefaultOrEmpty: true } or { Regiao: not ("SE" or "S" or "N" or "NE" or "CO") }
            => throw new ArgumentException("Pedido inválido."),
        { Prioritario: true } or { TotalCentavos: >= 100_000 } => "expresso",
        { Regiao: "N" or "NE", Unidades: >= 10 } => "regional",
        _ => "normal"
    };
}
