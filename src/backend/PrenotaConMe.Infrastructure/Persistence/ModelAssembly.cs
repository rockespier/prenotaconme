using System.Reflection;

namespace PrenotaConMe.Infrastructure.Persistence;

/// <summary>An assembly whose IEntityTypeConfiguration classes are applied to <see cref="AppDbContext"/>.</summary>
public sealed record ModelAssembly(Assembly Assembly);
