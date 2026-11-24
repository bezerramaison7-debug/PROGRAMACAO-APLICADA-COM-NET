using Application;
using Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Tests;

public sealed class PersistenciaTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MedeProjecaoTrackingJsonEConsultas()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var comandos = new Comandos();
        await using var db = new PedidosDb(new DbContextOptionsBuilder<PedidosDb>()
            .UseSqlite(conn).AddInterceptors(comandos).Options);
        await db.Database.MigrateAsync();
        var store = new PedidosStore(db);
        var cat = new Catalogo();
        Guid id = default;
        for (var i = 0; i < 120; i++)
        {
            var pedido = Contratos.Mapear(new(i % 2 == 0 ? "SE" : "NE", false, [new("A", 1, 200)]));
            id = pedido.Id.Value;
            await store.Criar(pedido, cat.Preparar(pedido), "lab", default);
        }
        db.ChangeTracker.Clear();
        comandos.Sql.Clear();
        var p1 = await store.Listar("lab", "normal", 1, 15, default);
        Assert.Equal(120, p1.Total);
        Assert.Equal(15, p1.Pedidos.Count);
        Assert.Equal(2, comandos.Sql.Count);
        output.WriteLine("Projeção e contagem em dois comandos:\n" + string.Join("\n", comandos.Sql));
        var p2 = await store.Listar("lab", "normal", 2, 15, default);
        Assert.Empty(p1.Pedidos.Select(p => p.Id).Intersect(p2.Pedidos.Select(p => p.Id)));
        Assert.Empty(db.ChangeTracker.Entries());

        foreach (var split in new[] { false, true })
        {
            comandos.Sql.Clear();
            var grafo = store.Filtrar("lab", null).OrderBy(p => p.Id).Take(15).Include(p => p.Itens);
            var itens = await (split ? grafo.AsSplitQuery() : grafo.AsSingleQuery()).ToListAsync();
            Assert.All(itens, p => Assert.Single(p.Itens));
            Assert.Equal(split ? 2 : 1, comandos.Sql.Count);
            output.WriteLine($"Include split={split}: {comandos.Sql.Count} comando(s).");
        }
        var json = db.Pedidos.Where(p => p.Meta.Regiao == "NE");
        output.WriteLine(json.ToQueryString());
        Assert.Equal(60, await json.CountAsync());
        var row = await json.FirstAsync(p => p.Id == id);
        comandos.Sql.Clear();
        row.Meta.Regiao = "S";
        await db.SaveChangesAsync();
        output.WriteLine(string.Join("\n", comandos.Sql));
        Assert.Equal(59, await json.CountAsync());

        var normal = await store.Buscar(id, "lab", default);
        Assert.Equal(normal, await store.BuscarCompilado(id, "lab"));
        Assert.Null(await store.BuscarCompilado(id, "outro"));
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 250; i++)
            await store.Buscar(id, "lab", default);
        var tempoNormal = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        for (var i = 0; i < 250; i++)
            await store.BuscarCompilado(id, "lab");
        output.WriteLine($"250 consultas: LINQ={tempoNormal:F2} ms; compilada={sw.Elapsed.TotalMilliseconds:F2} ms; SQLite={conn.ServerVersion}");
    }

    private sealed class Comandos : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
