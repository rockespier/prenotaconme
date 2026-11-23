using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Shared.Domain;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.UnitTests.Persistence;

public sealed class Note : TenantEntity
{
    public string Text { get; set; } = "";
}

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder) => builder.ToTable("notes");
}

public sealed class TenantIsolationTests
{
    private static readonly Guid TenantA = Guid.CreateVersion7();
    private static readonly Guid TenantB = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    private readonly string _database = Guid.NewGuid().ToString();

    private AppDbContext CreateContext(Guid? tenantId)
    {
        var tenant = new TenantContext();
        if (tenantId is { } id)
        {
            tenant.SetTenant(id);
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_database)
            .AddInterceptors(new AuditingInterceptor(tenant, new FixedTimeProvider(Now)))
            .Options;

        return new AppDbContext(options, tenant, [new ModelAssembly(typeof(Note).Assembly)]);
    }

    [Fact]
    public async Task Query_returns_only_current_tenant_rows()
    {
        await using (var a = CreateContext(TenantA))
        {
            a.Add(new Note { Text = "a" });
            await a.SaveChangesAsync();
        }

        await using (var b = CreateContext(TenantB))
        {
            b.Add(new Note { Text = "b" });
            await b.SaveChangesAsync();
        }

        await using var ctx = CreateContext(TenantA);
        var notes = await ctx.Set<Note>().ToListAsync();

        Assert.Single(notes);
        Assert.Equal("a", notes[0].Text);
        Assert.Equal(TenantA, notes[0].TenantId);
    }

    [Fact]
    public async Task Query_without_tenant_returns_nothing_unless_filter_ignored()
    {
        await using (var a = CreateContext(TenantA))
        {
            a.Add(new Note { Text = "a" });
            await a.SaveChangesAsync();
        }

        await using var ctx = CreateContext(null);

        Assert.Empty(await ctx.Set<Note>().ToListAsync());
        Assert.Single(await ctx.Set<Note>().IgnoreQueryFilters([AppDbContext.TenantFilter]).ToListAsync());
    }

    [Fact]
    public async Task Insert_stamps_tenant_and_created_at()
    {
        await using var ctx = CreateContext(TenantA);
        var note = new Note { Text = "x" };

        ctx.Add(note);
        await ctx.SaveChangesAsync();

        Assert.Equal(TenantA, note.TenantId);
        Assert.Equal(Now, note.CreatedAt);
        Assert.Null(note.UpdatedAt);
    }

    [Fact]
    public async Task Update_sets_updated_at()
    {
        await using var ctx = CreateContext(TenantA);
        var note = new Note { Text = "x" };
        ctx.Add(note);
        await ctx.SaveChangesAsync();

        note.Text = "y";
        await ctx.SaveChangesAsync();

        Assert.Equal(Now, note.UpdatedAt);
    }

    [Fact]
    public async Task Insert_without_tenant_in_scope_throws()
    {
        await using var ctx = CreateContext(null);
        ctx.Add(new Note());

        await Assert.ThrowsAsync<MissingTenantException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Insert_for_another_tenant_throws()
    {
        await using var ctx = CreateContext(TenantA);
        ctx.Add(new Note { TenantId = TenantB });

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Changing_tenant_id_throws()
    {
        await using var ctx = CreateContext(TenantA);
        var note = new Note();
        ctx.Add(note);
        await ctx.SaveChangesAsync();

        note.TenantId = TenantB;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
