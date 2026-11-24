using System.Collections.Concurrent;
using System.Net;
using Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Integration;

public sealed class FilaHttpTests
{
    [Fact]
    public async Task FilaCheiaFalhaIsoladaEDrenagemPeloWorker()
    {
        var fake = new ProcessadorObservavel();
        await using var fabrica = new HubFactory(s => s.Replace(ServiceDescriptor.Scoped<IProcessamento>(_ => fake)),
            new Dictionary<string, string?> { ["Fila:Capacidade"] = "1", ["Fila:Consumidores"] = "1" });
        using var operador = fabrica.Cliente();
        var primeiro = await ContratosTests.Criar(operador);
        var segundo = await ContratosTests.Criar(operador);
        var terceiro = await ContratosTests.Criar(operador);
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Task<HttpResponseMessage> Enfileirar(PedidoResponse p) =>
            operador.PostAsync($"/pedidos/{p.Id}/processamento", null, prazo.Token);

        try
        {
            Assert.Equal(HttpStatusCode.Accepted, (await Enfileirar(primeiro)).StatusCode);
            Assert.Equal(primeiro.Id, await fake.Entrou.Task.WaitAsync(prazo.Token));
            Assert.Equal(HttpStatusCode.Conflict, (await Enfileirar(primeiro)).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, (await Enfileirar(segundo)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Enfileirar(terceiro)).StatusCode);
        }
        finally
        {
            fake.Liberar.TrySetResult();
        }

        Assert.Equal(segundo.Id, await fake.Concluiu.Task.WaitAsync(prazo.Token));
        Assert.Equal([primeiro.Id, segundo.Id], fake.Vistos.ToArray());
    }

    [Fact]
    public async Task QuatroConsumidoresNaoExcedemUmSlotDePersistencia()
    {
        var fake = new MedidorBanco();
        await using var fabrica = new HubFactory(s => s.Replace(ServiceDescriptor.Scoped<IProcessamento>(_ => fake)),
            new Dictionary<string, string?> { ["Fila:Capacidade"] = "8", ["Fila:Consumidores"] = "4" });
        using var operador = fabrica.Cliente();
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pedidos = new List<PedidoResponse>();
        for (var i = 0; i < 4; i++)
            pedidos.Add(await ContratosTests.Criar(operador));
        try
        {
            foreach (var p in pedidos)
                Assert.Equal(HttpStatusCode.Accepted, (await operador.PostAsync($"/pedidos/{p.Id}/processamento", null, prazo.Token)).StatusCode);
            await fake.Primeiro.Task.WaitAsync(prazo.Token);
            var fila = fabrica.Services.GetRequiredService<Api.Fila>();
            while (fila.Leitor.Count != 0)
                await Task.Delay(5, prazo.Token);
            Assert.Equal(1, Volatile.Read(ref fake.Iniciados));
            Assert.Equal(0, Volatile.Read(ref fake.Violacoes));
        }
        finally
        {
            fake.Liberar.TrySetResult();
        }
        await fake.Concluiu.Task.WaitAsync(prazo.Token);
        Assert.Equal(4, Volatile.Read(ref fake.Iniciados));
        Assert.Equal(0, Volatile.Read(ref fake.Violacoes));
    }

    private sealed class MedidorBanco : IProcessamento
    {
        public readonly TaskCompletionSource Primeiro = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Liberar = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Concluiu = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Iniciados, Violacoes;
        private int ativos, concluidos;

        public async Task<PedidoAtualizado?> Processar(ProcessarPedidoJob job, CancellationToken token)
        {
            if (Interlocked.Increment(ref ativos) > 1)
                Interlocked.Increment(ref Violacoes);
            Interlocked.Increment(ref Iniciados);
            Primeiro.TrySetResult();
            try
            {
                await Liberar.Task.WaitAsync(token);
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref ativos);
                if (Interlocked.Increment(ref concluidos) == 4)
                    Concluiu.TrySetResult();
            }
        }
    }

    private sealed class ProcessadorObservavel : IProcessamento
    {
        public TaskCompletionSource<Guid> Entrou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Guid> Concluiu { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<Guid> Vistos { get; } = new();

        public async Task<PedidoAtualizado?> Processar(ProcessarPedidoJob job, CancellationToken token)
        {
            Vistos.Enqueue(job.Id);
            if (Entrou.TrySetResult(job.Id))
            {
                await Liberar.Task.WaitAsync(token);
                throw new InvalidOperationException("Falha de persistência simulada.");
            }
            Concluiu.TrySetResult(job.Id);
            return null;
        }
    }
}
