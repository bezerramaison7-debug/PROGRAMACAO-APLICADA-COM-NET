using Application;
using Microsoft.AspNetCore.Authentication;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api;

public static class Modulos
{
    public static IServiceCollection AddPedidos(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PedidosDb>(options => options.UseSqlite(
            config.GetConnectionString("Pedidos") ?? "Data Source=pedidos.db"));
        services.AddScoped<PedidosStore>();
        services.AddSingleton(new Catalogo());
        return services;
    }

    public static IServiceCollection AddProcessamento(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IProcessamento, ProcessamentoSql>();
        services.AddSingleton(_ => new Fila(Math.Clamp(config.GetValue("Fila:Capacidade", 16), 1, 1024)));
        services.AddSingleton(_ => new LimiteBanco(1));
        services.AddHostedService<Worker>();
        return services;
    }

    public static IServiceCollection AddNotificacoes(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<Assinaturas>();
        services.AddSingleton<INotificacoes, NotificacaoSignalR>();
        return services;
    }

    public static IServiceCollection AddSeguranca(this IServiceCollection services)
    {
        services.AddSingleton<Operadores>();
        services.AddAuthentication("Laboratorio").AddScheme<AuthenticationSchemeOptions, LaboratorioAuth>("Laboratorio", _ => { });
        services.AddAuthorization();
        services.AddOutputCache(options => options.AddPolicy("indicadores", p =>
            p.Expire(TimeSpan.FromSeconds(15)).SetVaryByQuery("categoria").Tag("indicadores")));
        return services;
    }
}
