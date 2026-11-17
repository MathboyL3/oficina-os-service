using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Os.Application.Auth;
using Os.Application.Saga;
using Os.Domain.Clientes;
using Os.Domain.Common;
using Os.Domain.Identidade;
using Os.Domain.OrdensServico;
using Os.Domain.Veiculos;
using Os.Infrastructure.Auth;
using Os.Infrastructure.Catalogo;
using Os.Infrastructure.Persistence;
using Os.Infrastructure.Persistence.Repositories;
using Os.Infrastructure.Saga;

namespace Os.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IClienteRepository, ClienteRepository>();
        services.AddScoped<IVeiculoRepository, VeiculoRepository>();
        services.AddScoped<IOrdemDeServicoRepository, OrdemDeServicoRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();

        services.AddScoped<IAplicadorStatusOs, AplicadorStatusOs>();
        services.AddScoped<ISagaConsulta, SagaConsulta>();

        services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        services.AddCatalogo(cfg);

        return services;
    }
}