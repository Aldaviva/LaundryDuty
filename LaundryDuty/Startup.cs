using Kasa;
using LaundryDuty;
using Pager.Duty;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(WindowsService.configureWindowsService)
    .ConfigureServices((context, services) => {
        services.AddHostedService<LaundryMonitor>();
        services.AddSingleton<PagerDutyManager, PagerDutyManagerImpl>();
        services.AddSingleton<IKasaOutlet>(s => {
            Configuration configuration = s.GetRequiredService<Configuration>();
            return new KasaOutlet(configuration.outletHostname, new Options {
                LoggerFactory  = s.GetService<ILoggerFactory>(),
                MaxAttempts    = configuration.outletMaxAttempts,
                ReceiveTimeout = TimeSpan.FromMilliseconds(configuration.outletTimeoutMilliseconds),
                SendTimeout    = TimeSpan.FromMilliseconds(configuration.outletTimeoutMilliseconds),
                RetryDelay     = TimeSpan.FromMilliseconds(configuration.outletRetryDelayMilliseconds)
            });
        });
        services.AddSingleton<IPagerDuty>(s => new PagerDuty(s.GetRequiredService<Configuration>().pagerDutyIntegrationKey));
        services.AddSingleton(_ => context.Configuration.Get<Configuration>()!);
    })
    .Build();

await host.RunAsync();