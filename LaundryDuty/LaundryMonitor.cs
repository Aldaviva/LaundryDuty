using Kasa;

namespace LaundryDuty;

public class LaundryMonitor: BackgroundService {

    private readonly ILogger<LaundryMonitor> logger;
    private readonly IKasaOutlet             outlet;
    private readonly PagerDutyManager        pagerDuty;
    private readonly Configuration           config;

    private LaundryMachineState? state;
    private string?              pagerdutyDedupKey;

    public LaundryMonitor(ILogger<LaundryMonitor> logger, IKasaOutlet outlet, PagerDutyManager pagerDuty, Configuration config) {
        this.logger    = logger;
        this.outlet    = outlet;
        this.pagerDuty = pagerDuty;
        this.config    = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                int livePowerMilliWatts = (await outlet.EnergyMeter.GetInstantaneousPowerUsage()).Power;

                LaundryMachineState? newState;
                if (livePowerMilliWatts >= config.minimumActiveMilliwatts) {
                    newState = LaundryMachineState.ACTIVE;
                } else if (livePowerMilliWatts > config.maximumIdleMilliwatts && livePowerMilliWatts < config.minimumActiveMilliwatts) {
                    newState = LaundryMachineState.COMPLETE;
                } else if (livePowerMilliWatts > 0 && livePowerMilliWatts <= config.maximumIdleMilliwatts) {
                    newState = LaundryMachineState.IDLE;
                } else {
                    newState = state;
                }

                if (state != null && state != newState) {
                    switch (newState) {
                        case LaundryMachineState.ACTIVE:
                            logger.LogInformation("Started a load of laundry.");
                            pagerdutyDedupKey = null;
                            break;
                        case LaundryMachineState.COMPLETE:
                            logger.LogInformation("Laundry is finished.");
                            pagerdutyDedupKey = pagerDuty.createIncident();
                            break;
                        case LaundryMachineState.IDLE when pagerdutyDedupKey is not null:
                            logger.LogInformation("Laundry is being emptied.");
                            pagerDuty.resolveIncident(pagerdutyDedupKey);
                            pagerdutyDedupKey = null;
                            break;
                        default:
                            break;
                    }
                }

                state = newState;

                await Task.Delay(config.pollingIntervalMilliseconds, stoppingToken);
            }
        } catch (Exception e) {
            logger.LogError(e, "{Message}", e.Message);
            Environment.Exit(1);
        }
    }

    private enum LaundryMachineState {

        IDLE,
        ACTIVE,
        COMPLETE

    }

}