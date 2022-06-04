using Kasa;
using LaundryDuty;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "LaundryDuty")
    .ConfigureServices(services => {
        services.AddHostedService<LaundryMonitor>();
        services.AddScoped<PagerDutyManager, PagerDutyManagerImpl>();
        services.AddSingleton<IKasaOutlet>(s => new KasaOutlet(s.GetRequiredService<Configuration>().outletHostname));
        services.AddScoped(s => s.GetRequiredService<IConfiguration>().Get<Configuration>());
    })
    .Build();

await host.RunAsync();