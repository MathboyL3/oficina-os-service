using Microsoft.Extensions.DependencyInjection;
using Os.Application.UseCases.Auth;
using Os.Application.UseCases.Clientes;
using Os.Application.UseCases.OrdensServico;
using Os.Application.UseCases.Veiculos;

namespace Os.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ClienteUseCases>();
        services.AddScoped<VeiculoUseCases>();
        services.AddScoped<OrdemServicoUseCases>();
        services.AddScoped<AuthUseCases>();
        return services;
    }
}