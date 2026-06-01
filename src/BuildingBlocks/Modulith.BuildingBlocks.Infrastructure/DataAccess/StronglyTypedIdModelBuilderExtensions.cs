using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Modulith.BuildingBlocks.Domain;

namespace Modulith.BuildingBlocks.Infrastructure.DataAccess;

public static class StronglyTypedIdModelBuilderExtensions
{
    public static void ApplyStronglyTypedIdConverters(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var propertyType = property.ClrType;
                if (!propertyType.IsSubclassOfRawGeneric(typeof(StronglyTypedId<>)))
                {
                    continue;
                }

                var valueType = propertyType.BaseType!.GetGenericArguments()[0];
                var converterType = typeof(StronglyTypedIdValueConverter<,>).MakeGenericType(propertyType, valueType);
                var converter = Activator.CreateInstance(converterType);
                property.SetValueConverter((ValueConverter?)converter);
            }
        }
    }

    private static bool IsSubclassOfRawGeneric(this Type candidateType, Type rawGenericType)
    {
        var currentType = candidateType;
        while (currentType != typeof(object) && currentType is not null)
        {
            var current = currentType.IsGenericType ? currentType.GetGenericTypeDefinition() : currentType;
            if (current == rawGenericType)
            {
                return true;
            }

            currentType = currentType.BaseType!;
        }

        return false;
    }
}
