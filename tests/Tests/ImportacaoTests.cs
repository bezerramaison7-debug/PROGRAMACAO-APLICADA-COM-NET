using System.Text;
using Application;

namespace Tests;

public sealed class ImportacaoTests
{
    [Theory]
    [InlineData("A1;1;1")]
    [InlineData("SKU;10000;100000000")]
    [InlineData("B2;+1;0002")]
    public void Equivalencia(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        Assert.Equal(Importacao.Baseline(bytes), Importacao.Otimizado(bytes));
    }

    [Theory]
    [InlineData("A;0;1")]
    [InlineData("A;1;-1")]
    [InlineData("A; 1;1")]
    [InlineData("A;1;999999999999999999999")]
    [InlineData("a;1;1")]
    [InlineData("A;1;1;")]
    [InlineData("A;1;1\0")]
    public void RejeitaLinhaInvalida(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        Assert.Throws<FormatException>(() => Importacao.Baseline(bytes));
        Assert.Throws<FormatException>(() => Importacao.Otimizado(bytes));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1600)]
    public async Task LeLoteIncremental(int qtd)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("A;2;3\r\n", qtd))));
        Assert.Equal(qtd, (await Importacao.LerAsync(stream, TestContextToken())).Count);
    }

    [Fact]
    public async Task CancelamentoEExcessoDeBytes()
    {
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();
        using var stream = new MemoryStream("A;1;1"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Importacao.LerAsync(stream, cancelado.Token));
        using var lote = new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("A;1;1\n", 180000))));
        await Assert.ThrowsAsync<FormatException>(() => Importacao.LerAsync(lote, TestContextToken()));
    }

    private static CancellationToken TestContextToken() => CancellationToken.None;
}
