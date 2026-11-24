using Application;
using Domain;

namespace Tests;

public sealed class ArquiteturaTests
{
    [Fact]
    public void DomainEApplicationNaoDependemDeImplementacoes()
    {
        Assert.DoesNotContain(typeof(Pedido).Assembly.GetReferencedAssemblies(),
            a => a.Name is "Api" or "Application" or "Infrastructure" || a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(CriarPedidoRequest).Assembly.GetReferencedAssemblies(),
            a => a.Name is "Api" or "Infrastructure" || a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
