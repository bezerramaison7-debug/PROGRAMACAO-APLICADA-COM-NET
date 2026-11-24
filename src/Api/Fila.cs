using Application;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;

namespace Api;

public enum Inclusao { Aceito, Cheia, Duplicado, Encerrada }

public sealed class Fila : IDisposable
{
    private readonly Channel<ProcessarPedidoJob> canal;
    private readonly ConcurrentDictionary<Guid, byte> emAndamento = new();
    private readonly Meter meter = new("HubPedidos.Fila");
    private readonly Counter<long> aceitosMetrica;
    private readonly Counter<long> rejeitadosMetrica;
    private long aceitos, rejeitados, processados, falhas;
    private int finalizada;

    public Fila(int capacidade)
    {
        canal = Channel.CreateBounded<ProcessarPedidoJob>(new BoundedChannelOptions(capacidade)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
        meter.CreateObservableGauge("fila.profundidade", () => canal.Reader.Count);
        aceitosMetrica = meter.CreateCounter<long>("fila.aceitos");
        rejeitadosMetrica = meter.CreateCounter<long>("fila.rejeitados");
    }

    public ChannelReader<ProcessarPedidoJob> Leitor => canal.Reader;
    public object Medidas() => new
    {
        profundidade = Leitor.Count,
        aceitos = Interlocked.Read(ref aceitos),
        rejeitados = Interlocked.Read(ref rejeitados),
        processados = Interlocked.Read(ref processados),
        falhas = Interlocked.Read(ref falhas),
        descartados = 0
    };

    public Inclusao Tentar(ProcessarPedidoJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref finalizada) != 0)
            return Inclusao.Encerrada;
        if (!emAndamento.TryAdd(job.Id, 0))
            return Inclusao.Duplicado;
        if (canal.Writer.TryWrite(job))
        {
            Interlocked.Increment(ref aceitos);
            aceitosMetrica.Add(1);
            return Inclusao.Aceito;
        }
        emAndamento.TryRemove(job.Id, out _);
        if (Volatile.Read(ref finalizada) != 0)
            return Inclusao.Encerrada;
        Interlocked.Increment(ref rejeitados);
        rejeitadosMetrica.Add(1);
        return Inclusao.Cheia;
    }

    public void Finalizar(ProcessarPedidoJob job, bool sucesso)
    {
        if (sucesso)
            Interlocked.Increment(ref processados);
        else
            Interlocked.Increment(ref falhas);
        emAndamento.TryRemove(job.Id, out _);
    }

    public void Fechar()
    {
        Interlocked.Exchange(ref finalizada, 1);
        canal.Writer.TryComplete();
    }

    public void Dispose() => meter.Dispose();
}

public sealed class LimiteBanco(int capacidade) : IDisposable
{
    public SemaphoreSlim Semaforo { get; } = new(capacidade, capacidade);
    public void Dispose() => Semaforo.Dispose();
}

public sealed class Worker(Fila fila, LimiteBanco limite, IServiceScopeFactory scopes,
    INotificacoes notificacoes, IConfiguration config, ILogger<Worker> logger) : BackgroundService
{
    private readonly CancellationTokenSource abortar = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Clamp(config.GetValue("Fila:Consumidores", 2), 1, 16))
            .Select(_ => Consumir(abortar.Token)));

    private async Task Consumir(CancellationToken token)
    {
        await foreach (var job in fila.Leitor.ReadAllAsync(token))
        {
            var inicio = Stopwatch.GetTimestamp();
            var sucesso = false;
            try
            {
                PedidoAtualizado? evento;
                await limite.Semaforo.WaitAsync(token);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    evento = await scope.ServiceProvider.GetRequiredService<IProcessamento>().Processar(job, token);
                }
                finally
                {
                    limite.Semaforo.Release();
                }
                sucesso = true;
                if (evento is not null)
                    await notificacoes.Publicar(job.Dono, evento, token);
                logger.LogInformation("Pedido {Pedido} concluído em {DuracaoMs:F2} ms.", job.Id,
                    Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao executar ou notificar {Pedido}. Persistido: {Sucesso}.", job.Id, sucesso);
            }
            finally
            {
                fila.Finalizar(job, sucesso);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        fila.Fechar();
        using var prazo = cancellationToken.Register(abortar.Cancel);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        abortar.Cancel();
        abortar.Dispose();
        base.Dispose();
    }
}

public sealed class NotificacaoNula : INotificacoes
{
    public Task Publicar(string dono, PedidoAtualizado evento, CancellationToken token) => Task.CompletedTask;
}
