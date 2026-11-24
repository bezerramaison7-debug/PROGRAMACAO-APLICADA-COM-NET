namespace Application;

public sealed record ProcessarPedidoJob(Guid Id, string Dono);
public sealed record PedidoAtualizado(Guid Id, string Estado, int Revisao, DateTimeOffset Instante);

public interface IProcessamento
{
    Task<PedidoAtualizado?> Processar(ProcessarPedidoJob job, CancellationToken token);
}

public interface INotificacoes
{
    Task Publicar(string dono, PedidoAtualizado evento, CancellationToken token);
}
