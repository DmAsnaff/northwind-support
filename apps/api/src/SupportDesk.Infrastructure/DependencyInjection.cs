using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.Infrastructure.Services;

namespace SupportDesk.Infrastructure;

/// <summary>
/// Wires the domain's repositories and the application's abstractions to their SQL Server /
/// system implementations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. See README.md for local setup.");

        services.AddDbContext<SupportDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SupportDbContext>());

        services.AddSingleton<IClock, SystemClock>();

        var slaSettings = BindSlaSettings(configuration);
        services.AddSingleton(slaSettings);
        services.AddSingleton<SlaPolicy>();

        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        services.AddScoped<ITicketQueries, TicketQueries>();
        services.AddScoped<ICustomerQueries, CustomerQueries>();
        services.AddScoped<IAgentQueries, AgentQueries>();
        services.AddScoped<ICategoryQueries, CategoryQueries>();

        services.AddScoped<SupportDbSeeder>();

        return services;
    }

    private static SlaSettings BindSlaSettings(IConfiguration configuration)
    {
        var settings = new SlaSettings();
        var section = configuration.GetSection(SlaSettings.SectionName);
        if (!section.Exists())
        {
            return settings;
        }

        if (double.TryParse(section["PremiumCustomerMultiplier"], CultureInfo.InvariantCulture, out var multiplier))
        {
            settings.PremiumCustomerMultiplier = multiplier;
        }

        if (double.TryParse(section["MinimumWindowHours"], CultureInfo.InvariantCulture, out var minHours))
        {
            settings.MinimumWindowHours = minHours;
        }

        if (double.TryParse(section["AtRiskThresholdPercent"], CultureInfo.InvariantCulture, out var threshold))
        {
            settings.AtRiskThresholdPercent = threshold;
        }

        var baseWindows = section.GetSection("BaseWindowHours");
        foreach (var priority in Enum.GetValues<TicketPriority>())
        {
            if (double.TryParse(baseWindows[priority.ToString()], CultureInfo.InvariantCulture, out var hours))
            {
                settings.BaseWindowHours[priority] = hours;
            }
        }

        return settings;
    }
}
