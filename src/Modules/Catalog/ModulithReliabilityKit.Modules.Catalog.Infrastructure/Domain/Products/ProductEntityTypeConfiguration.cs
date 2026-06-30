using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Domain.Products;

internal sealed class ProductEntityTypeConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(x => x.Id);

        // ProductId conversion is applied globally by ApplyStronglyTypedIdConverters.
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired().HasColumnName("name");
        builder.Property(x => x.IsActive).IsRequired().HasColumnName("is_active");
        builder.Property(x => x.CreatedOnUtc).IsRequired().HasColumnName("created_on_utc");

        builder.OwnsOne(x => x.Price, price =>
        {
            price.Property(p => p.Amount).HasColumnName("price_amount").HasColumnType("numeric(18,2)").IsRequired();
            price.Property(p => p.Currency).HasColumnName("price_currency").HasMaxLength(3).IsRequired();
        });

        builder.Navigation(x => x.Price).IsRequired();

        builder.Ignore(x => x.DomainEvents);
    }
}
