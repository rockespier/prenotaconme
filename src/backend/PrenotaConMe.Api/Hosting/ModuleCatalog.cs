using PrenotaConMe.Modules.Calendar;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Chatbot;
using PrenotaConMe.Modules.Finance;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Notifications;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Tenancy;
using PrenotaConMe.Modules.Users;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Api.Hosting;

/// <summary>Explicit list of business modules. Add each new module here.</summary>
internal static class ModuleCatalog
{
    public static readonly IModule[] All =
    [
        new TenancyModule(),
        new UsersModule(),
        new LocationsModule(),
        new CatalogModule(),
        new StaffModule(),
        new SchedulingModule(),
        new NotificationsModule(),
        new ChatbotModule(),
        new CalendarModule(),
        new FinanceModule(),
    ];
}
