using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Modulith.BuildingBlocks.Domain;

namespace Modulith.BuildingBlocks.Infrastructure.DataAccess;

public sealed class StronglyTypedIdValueConverter<TStronglyTypedId, TValue>
    : ValueConverter<TStronglyTypedId, TValue>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    public StronglyTypedIdValueConverter()
        : base(
            id => id.Value,
            value => (TStronglyTypedId)Activator.CreateInstance(typeof(TStronglyTypedId), value)!)
    {
    }
}
