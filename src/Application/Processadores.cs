using System.Collections.Frozen;
using System.Reflection;
using Domain;

namespace Application;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ProcessadorPedidoAttribute(string categoria, int versao) : Attribute
{
    public string Categoria { get; } = categoria;
    public int Versao { get; } = versao;
}

public interface IProcessadorPedido
{
    PedidoResponse Preparar(Pedido pedido, string categoria);
}

[ProcessadorPedido("normal", 1)]
[ProcessadorPedido("regional", 1)]
public sealed class Convencional : IProcessadorPedido
{
    public PedidoResponse Preparar(Pedido pedido, string categoria) => new(pedido.Id.Value, pedido.TotalCentavos, categoria);
}

[ProcessadorPedido("expresso", 1)]
public sealed class Expresso : IProcessadorPedido
{
    public PedidoResponse Preparar(Pedido pedido, string categoria) => new(pedido.Id.Value, pedido.TotalCentavos, categoria);
}

public sealed class Catalogo
{
    private readonly FrozenDictionary<(string, int), IProcessadorPedido> processadores;

    public Catalogo() : this(typeof(Convencional).Assembly.GetTypes()) { }

    public Catalogo(IEnumerable<Type> tipos)
    {
        var catalogo = new Dictionary<(string, int), IProcessadorPedido>();
        foreach (var tipo in tipos.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var atributos = tipo.GetCustomAttributes<ProcessadorPedidoAttribute>().ToArray();
            if (atributos.Length == 0)
                continue;
            if (!tipo.IsClass || tipo.IsAbstract || !typeof(IProcessadorPedido).IsAssignableFrom(tipo) ||
                tipo.GetConstructor(Type.EmptyTypes) is null)
                throw new InvalidOperationException($"Processador com configuração inválida: {tipo.Name}");
            var processador = (IProcessadorPedido)Activator.CreateInstance(tipo)!;
            foreach (var a in atributos)
                if (string.IsNullOrWhiteSpace(a.Categoria) || a.Versao < 1 || !catalogo.TryAdd((a.Categoria, a.Versao), processador))
                    throw new InvalidOperationException($"Metadado inválido ou duplicado: {a.Categoria}/{a.Versao}");
        }
        processadores = catalogo.ToFrozenDictionary();
    }

    public PedidoResponse Preparar(Pedido pedido)
    {
        var categoria = Contratos.Classificar(pedido);
        return processadores[(categoria, 1)].Preparar(pedido, categoria);
    }
}
