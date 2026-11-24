using Api;
using Application;

namespace Tests;

public sealed class FilaTests
{
    [Fact]
    public async Task ExplicitaFilaCheiaDuplicidadeCancelamentoEDrenagem()
    {
        using var fila = new Fila(1);
        var job = new ProcessarPedidoJob(Guid.NewGuid(), "teste");
        Assert.Equal(Inclusao.Aceito, fila.Tentar(job, default));
        Assert.Equal(Inclusao.Duplicado, fila.Tentar(job, default));
        Assert.Equal(Inclusao.Cheia, fila.Tentar(new(Guid.NewGuid(), "teste"), default));
        Assert.Throws<OperationCanceledException>(() => fila.Tentar(job, new CancellationToken(true)));
        fila.Fechar();
        Assert.Equal(Inclusao.Encerrada, fila.Tentar(job, default));
        Assert.Equal(job, await fila.Leitor.ReadAsync());
        Assert.False(await fila.Leitor.WaitToReadAsync());
    }
}
