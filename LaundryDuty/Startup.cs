using System.Runtime.CompilerServices;
using Kasa;
using LaundryDuty;
using Pager.Duty;

[assembly: InternalsVisibleTo("Tests")]

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "LaundryDuty")
    .ConfigureServices(services => {
        services.AddHostedService<LaundryMonitor>();
        services.AddSingleton<PagerDutyManager, PagerDutyManagerImpl>();
        services.AddSingleton<IKasaOutlet>(s => new KasaOutlet(s.GetRequiredService<Configuration>().outletHostname));
        services.AddSingleton<IPagerDuty>(s => new PagerDuty(s.GetRequiredService<Configuration>().pagerDutyIntegrationKey));
        services.AddSingleton(s => s.GetRequiredService<IConfiguration>().Get<Configuration>()!);
    })
    .Build();

await host.RunAsync();