using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Catalog.Domain;

/// <summary>Something a client can book (e.g. "Haircut, 30 min, €20"), offered at one or more locations.</summary>
public sealed class Service : TenantEntity
{
    private Service()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Multiple of 30 minutes (the booking grid).</summary>
    public int DurationMinutes { get; private set; }

    public decimal Price { get; private set; }

    public bool IsActive { get; private set; } = true;

    public List<ServiceLocation> Locations { get; private set; } = [];

    public static Service Create(ServiceData data)
    {
        var service = new Service();
        service.Update(data);
        return service;
    }

    public void Update(ServiceData data)
    {
        Name = data.Name.Trim();
        Description = string.IsNullOrWhiteSpace(data.Description) ? null : data.Description.Trim();
        DurationMinutes = data.DurationMinutes;
        Price = decimal.Round(data.Price, 2);
        IsActive = data.IsActive;

        var wanted = data.LocationIds.ToHashSet();
        Locations.RemoveAll(l => !wanted.Contains(l.LocationId));
        foreach (var id in wanted.Where(id => Locations.All(l => l.LocationId != id)))
        {
            Locations.Add(new ServiceLocation(Id, id));
        }
    }
}

public sealed class ServiceLocation : TenantEntity
{
    public ServiceLocation(Guid serviceId, Guid locationId)
    {
        ServiceId = serviceId;
        LocationId = locationId;
    }

    public Guid ServiceId { get; private set; }

    public Guid LocationId { get; private set; }
}

public sealed record ServiceData(
    string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive, IReadOnlyCollection<Guid> LocationIds);
