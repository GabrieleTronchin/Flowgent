namespace Flowgent.Persistence;

using Flowgent.Runtime.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public static class PersistenceServiceExtensions
{
    /// <summary>
    /// Registers the EF Core InMemory store for runs, events and the inbox. Each call uses its own database,
    /// so in-process hosts (tests) never share data.
    /// </summary>
    public static IServiceCollection AddFlowgentPersistence(this IServiceCollection services)
    {
        var databaseName = $"flowgent-{Guid.NewGuid():N}";
        services.AddDbContextFactory<EngineDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<SequenceGenerator>();
        services.AddSingleton<IRunStore, EfRunStore>();
        services.AddSingleton<IInboxStore, EfInboxStore>();
        return services;
    }
}
