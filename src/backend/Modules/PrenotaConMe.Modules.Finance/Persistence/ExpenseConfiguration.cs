using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Finance.Domain;
using PrenotaConMe.Modules.Locations.Domain;

namespace PrenotaConMe.Modules.Finance.Persistence;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses");
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(Expense.NameMaxLength).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(12, 2);
        // A deleted location's expenses become general ones instead of blocking the delete.
        builder.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.SetNull);
    }
}
