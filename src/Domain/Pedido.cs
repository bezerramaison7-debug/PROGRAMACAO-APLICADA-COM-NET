using System.Collections.Immutable;

namespace Domain;

public sealed record PedidoId
{
    public Guid Value { get; }

    public PedidoId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("O identificador deve ser válido.", nameof(value));
        Value = value;
    }

    public static bool TryParse(string text, out PedidoId? id)
    {
        id = Guid.TryParse(text, out var value) && value != Guid.Empty ? new(value) : null;
        return id is not null;
    }
}

public sealed record ItemPedido
{
    public string Sku { get; }
    public int Quantidade { get; }
    public long PrecoCentavos { get; }

    public ItemPedido(string sku, int quantidade, long precoCentavos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidade);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantidade, 10_000);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(precoCentavos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precoCentavos, 100_000_000);
        Sku = sku;
        Quantidade = quantidade;
        PrecoCentavos = precoCentavos;
    }
}

public sealed record Pedido(PedidoId Id, string Regiao, bool Prioritario, ImmutableArray<ItemPedido> Itens)
{
    public long TotalCentavos => Itens.Sum(item => item.Quantidade * item.PrecoCentavos);
    public int Unidades => Itens.Sum(item => item.Quantidade);
}
