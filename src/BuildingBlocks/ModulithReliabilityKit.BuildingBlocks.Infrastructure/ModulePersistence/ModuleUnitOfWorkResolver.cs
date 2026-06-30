using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Application;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;

public sealed class ModuleUnitOfWorkResolver : IUnitOfWorkResolver
{
    private readonly IReadOnlyCollection<ModulePersistenceRegistration> _registrations;
    private readonly IServiceProvider _serviceProvider;

    public ModuleUnitOfWorkResolver(
        IEnumerable<ModulePersistenceRegistration> registrations,
        IServiceProvider serviceProvider)
    {
        _registrations = registrations.ToList();
        _serviceProvider = serviceProvider;
    }

    public IUnitOfWork Resolve(Type requestType)
    {
        var registration = _registrations.LastOrDefault(x => x.RequestAssembly == requestType.Assembly);

        if (registration is null)
        {
            return _serviceProvider.GetRequiredService<IUnitOfWork>();
        }

        var serviceType = typeof(IModuleUnitOfWork<>).MakeGenericType(registration.DbContextType);
        return (IUnitOfWork)_serviceProvider.GetRequiredService(serviceType);
    }
}
