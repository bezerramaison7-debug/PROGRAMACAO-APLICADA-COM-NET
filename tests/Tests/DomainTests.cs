using Application;
using Domain;

namespace Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData("SE", false, 1, 99999, "normal")]
    [InlineData("SE", false, 1, 100000, "expresso")]
    [InlineData("SE", true, 1, 1, "expresso")]
    [InlineData("N", false, 9, 1, "normal")]
    [InlineData("NE", false, 10, 1, "regional")]
    [InlineData("S", false, 10, 1, "normal")]
    public void ClassificaNosLimites(string regiao, bool prioridade, int qtd, long preco, string categoria)
    {
        var pedido = Contratos.Mapear(new(regiao, prioridade, [new("A", qtd, preco)]));
        Assert.Equal(categoria, new Catalogo().Preparar(pedido).Categoria);
    }

    [Fact]
    public void ValidaECopia()
    {
        var dto = new CriarPedidoRequest("SE", false, [new("SKU", 1, 10)]);
        var copia = dto with { Prioritario = true };
        Assert.False(dto.Prioritario);
        Assert.True(copia.Prioritario);
        Assert.Equal(dto, dto with { });
        Assert.Throws<ArgumentException>(() => Contratos.Mapear(dto with { Regiao = "XX" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemPedido("A", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemPedido("A", 1, -1));
        Assert.False(PedidoId.TryParse(Guid.Empty.ToString(), out _));
        Assert.True(PedidoId.TryParse(Guid.NewGuid().ToString(), out _));
        Assert.Throws<InvalidOperationException>(() => new Catalogo([typeof(Expresso), typeof(Expresso)]));
    }
}
