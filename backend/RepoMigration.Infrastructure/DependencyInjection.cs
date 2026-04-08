using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoMigration.Core.Contracts;
using RepoMigration.Infrastructure.Services;

namespace RepoMigration.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;

        services.AddHttpClient("ado");
        services.AddHttpClient("github", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
        });

        services.AddScoped<IAdoDevOpsService, AdoDevOpsService>();
        services.AddScoped<IGitHubService, GitHubService>();
        services.AddScoped<IMigrationOrchestrator, MigrationOrchestrator>();
        services.AddSingleton<IGhCliPathResolver, GhCliPathResolver>();
        services.AddScoped<MigrationJobExecutor>();

        return services;
    }
}
