using System.Collections.Concurrent;
using System.Security.Claims;
using Application;
using Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;

namespace Api;

public interface IPedidoCliente
{
    Task PedidoAtualizado(PedidoAtualizado evento);
}

public sealed record Assinatura(string Conexao, Guid Pedido, string Dono, string Credencial);

public sealed class Assinaturas
{
    private readonly ConcurrentDictionary<(string, Guid), Assinatura> entradas = new();
    public IEnumerable<Assinatura> Para(Guid id, string dono) =>
        entradas.Values.Where(a => a.Pedido == id && a.Dono == dono);

    public void Adicionar(Assinatura a) => entradas[(a.Conexao, a.Pedido)] = a;
    public void Remover(string conexao, Guid id) => entradas.TryRemove((conexao, id), out _);
    public void Desconectar(string conexao)
    {
        foreach (var chave in entradas.Keys.Where(k => k.Item1 == conexao))
            entradas.TryRemove(chave, out _);
    }
    public static string Grupo(string dono, Guid id) => $"{dono}/{id:N}";
}

[Authorize]
public sealed class PedidosHub(PedidosStore store, Operadores operadores, Assinaturas assinaturas)
    : Hub<IPedidoCliente>
{
    public async Task Assinar(Guid id)
    {
        var dono = Context.UserIdentifier!;
        var hash = Context.User!.FindFirstValue("credencial")!;
        if (!operadores.Ativo(dono, hash) || await store.Buscar(id, dono, Context.ConnectionAborted) is null)
            throw new HubException("Acesso ao pedido negado.");
        var assinatura = new Assinatura(Context.ConnectionId, id, dono, hash);
        assinaturas.Adicionar(assinatura);
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Assinaturas.Grupo(dono, id), Context.ConnectionAborted);
        }
        catch
        {
            assinaturas.Remover(Context.ConnectionId, id);
            throw;
        }
    }

    public async Task Desassinar(Guid id)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Assinaturas.Grupo(Context.UserIdentifier!, id));
        assinaturas.Remover(Context.ConnectionId, id);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        assinaturas.Desconectar(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed class NotificacaoSignalR(IHubContext<PedidosHub, IPedidoCliente> hub,
    Operadores operadores, Assinaturas assinaturas, IOutputCacheStore cache) : INotificacoes
{
    public async Task Publicar(string dono, PedidoAtualizado evento, CancellationToken token)
    {
        await cache.EvictByTagAsync("indicadores", token);
        var grupo = Assinaturas.Grupo(dono, evento.Id);
        foreach (var a in assinaturas.Para(evento.Id, dono))
        {
            if (operadores.Ativo(dono, a.Credencial))
                continue;
            await hub.Groups.RemoveFromGroupAsync(a.Conexao, grupo, token);
            assinaturas.Remover(a.Conexao, a.Pedido);
        }
        await hub.Clients.Group(grupo).PedidoAtualizado(evento);
    }
}
