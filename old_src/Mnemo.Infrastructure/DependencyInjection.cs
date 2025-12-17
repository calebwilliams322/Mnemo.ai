using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Application.Interfaces;
using Mnemo.Infrastructure.Data;
using Mnemo.Infrastructure.Services;
using Supabase;

namespace Mnemo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string supabaseUrl,
        string supabaseKey)
    {
        // Add DbContext
        services.AddDbContext<MnemoDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
            }));

        // Add Supabase client
        services.AddSingleton<Client>(_ =>
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };
            return new Client(supabaseUrl, supabaseKey, options);
        });

        // Add services
        services.AddScoped<IStorageService, SupabaseStorageService>();

        return services;
    }
}
