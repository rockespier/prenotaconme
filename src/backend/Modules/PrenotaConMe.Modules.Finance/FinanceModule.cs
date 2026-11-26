using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Modules.Finance;

/// <summary>F4: expenses, monthly income/expense report by location, staff and service, PDF/Excel export. Depends on Scheduling.</summary>
public sealed class FinanceModule : IModule
{
    public string Name => "finance";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        // Free for businesses under 1M USD revenue (https://www.questpdf.com/license/).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => FinanceEndpoints.Map(endpoints);
}
