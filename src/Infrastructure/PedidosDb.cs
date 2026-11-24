using Application;
using Domain;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Infrastructure;

public sealed class PedidoRow
{
    public Guid Id { get; set; }
    public string Dono { get; set; } = "";
    public long TotalCentavos { get; set; }
    public string Categoria { get; set; } = "";
    public string Estado { get; set; } = "recebido";
    [ConcurrencyCheck]
    public int Revisao { get; set; } = 1;
    public MetaPedido Meta { get; set; } = new();
    public List<ItemRow> Itens { get; set; } = [];
}

public sealed class MetaPedido
{
    public string Regiao { get; set; } = "";
    public bool Prioritario { get; set; }
}

public sealed class ItemRow
{
    public int Id { get; set; }
    public Guid PedidoId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantidade { get; set; }
    public long PrecoCentavos { get; set; }
}

public sealed class PedidosDb(DbContextOptions<PedidosDb> options) : DbContext(options)
{
    public DbSet<PedidoRow> Pedidos => Set<PedidoRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PedidoRow>(b =>
        {
            b.HasIndex(p => new { p.Dono, p.Categoria, p.Id });
            b.HasIndex(p => new { p.Dono, p.Id });
            b.Property(p => p.Dono).HasMaxLength(100);
            b.Property(p => p.Categoria).HasMaxLength(16);
            b.Property(p => p.Estado).HasMaxLength(16);
            b.ComplexProperty(p => p.Meta, meta => meta.ToJson());
            b.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId);
        });
        model.Entity<ItemRow>().Property(i => i.Sku).HasMaxLength(100);
    }
}

public sealed class PedidosStore(PedidosDb db)
{
    private static readonly Func<PedidosDb, Guid, string, Task<PedidoResumo?>> ConsultaCompilada =
        EF.CompileAsyncQuery((PedidosDb db, Guid id, string dono) => db.Pedidos.AsNoTracking()
            .Where(p => p.Id == id && p.Dono == dono)
            .Select(p => new PedidoResumo(p.Id, p.TotalCentavos, p.Categoria, p.Estado)).FirstOrDefault());

    public async Task Criar(Pedido pedido, PedidoResponse resposta, string dono, CancellationToken token)
    {
        db.Pedidos.Add(new()
        {
            Id = pedido.Id.Value,
            Dono = dono,
            TotalCentavos = resposta.TotalCentavos,
            Categoria = resposta.Categoria,
            Meta = new() { Regiao = pedido.Regiao, Prioritario = pedido.Prioritario },
            Itens = pedido.Itens.Select(i => new ItemRow
            {
                Sku = i.Sku,
                Quantidade = i.Quantidade,
                PrecoCentavos = i.PrecoCentavos
            }).ToList()
        });
        await db.SaveChangesAsync(token);
    }

    public IQueryable<PedidoResumo> Consulta(Guid id, string dono) => db.Pedidos.AsNoTracking()
        .Where(p => p.Id == id && p.Dono == dono)
        .Select(p => new PedidoResumo(p.Id, p.TotalCentavos, p.Categoria, p.Estado));

    public Task<PedidoResumo?> Buscar(Guid id, string dono, CancellationToken token)
        => Consulta(id, dono).FirstOrDefaultAsync(token);

    public Task<PedidoResumo?> BuscarCompilado(Guid id, string dono) => ConsultaCompilada(db, id, dono);

    public IQueryable<PedidoRow> Filtrar(string dono, string? categoria) => db.Pedidos.AsNoTracking()
        .Where(p => p.Dono == dono && (categoria == null || p.Categoria == categoria));

    public static IQueryable<PedidoResumo> ProjetarPagina(IQueryable<PedidoRow> query, int pagina, int tamanho) =>
        query.OrderBy(p => p.Id).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(p => new PedidoResumo(p.Id, p.TotalCentavos, p.Categoria, p.Estado));

    public async Task<Pagina> Listar(string dono, string? categoria, int pagina, int tamanho, CancellationToken token)
    {
        var query = Filtrar(dono, categoria);
        var total = await query.CountAsync(token);
        var resultado = await ProjetarPagina(query, pagina, tamanho).ToListAsync(token);
        return new(total, resultado);
    }
}

public sealed class ProcessamentoSql(PedidosDb db) : IProcessamento
{
    public async Task<PedidoAtualizado?> Processar(ProcessarPedidoJob job, CancellationToken token)
    {
        var linha = await db.Pedidos.SingleOrDefaultAsync(p => p.Id == job.Id && p.Dono == job.Dono, token);
        if (linha is null || linha.Estado == "processado")
            return null;
        linha.Estado = "processado";
        linha.Revisao++;
        await db.SaveChangesAsync(token);
        return new(linha.Id, linha.Estado, linha.Revisao, DateTimeOffset.UtcNow);
    }
}
