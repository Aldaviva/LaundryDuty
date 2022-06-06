using Kasa;

namespace LaundryDuty;

public class LaundryMonitor: BackgroundService {

    private readonly ILogger<LaundryMonitor> logger;
    private readonly IKasaOutlet             outlet;
    private readonly PagerDutyManager        pagerDutyManager;
    private readonly Configuration           config;

    internal LaundryMachineState? state;
    internal string?              pagerdutyDedupKey;

    public LaundryMonitor(ILogger<LaundryMonitor> logger, IKasaOutlet outlet, PagerDutyManager pagerDutyManager, Configuration config) {
        this.logger           = logger;
        this.outlet           = outlet;
        this.pagerDutyManager = pagerDutyManager;
        this.config           = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                await executeOnce();
                await Task.Delay(config.pollingIntervalMilliseconds, stoppingToken);
            }
        } catch (Exception e) when (e is not TaskCanceledException) {
            logger.LogError(e, "{Message}", e.Message);
            Environment.Exit(1);
        }
    }

    internal async Task executeOnce() {
        int powerMilliwatts = (await outlet.EnergyMeter.GetInstantaneousPowerUsage()).Power;

        LaundryMachineState newState = getNewState(powerMilliwatts, state, config);

        if (state != null && state != newState) {
            await onStateChange(newState);
        }

        state = newState;
        logger.LogDebug("Laundry machine is {state}", state);
    }

    internal static LaundryMachineState getNewState(int powerMilliwatts, LaundryMachineState? oldState, Configuration config) {
        LaundryMachineState newState;
        if (config.minimumActiveMilliwatts <= powerMilliwatts) {
            newState = LaundryMachineState.ACTIVE;
        } else if (config.maximumIdleMilliwatts <= powerMilliwatts && powerMilliwatts < config.minimumActiveMilliwatts && oldState == LaundryMachineState.ACTIVE) {
            newState = LaundryMachineState.COMPLETE;
        } else if (0 < powerMilliwatts && powerMilliwatts < config.maximumIdleMilliwatts) {
            newState = LaundryMachineState.IDLE;
        } else {
            // 0 mW: when either complete or idle, the power is sporadically read as 0 mW, so just ignore those readings and wait for another one
            newState = oldState ?? LaundryMachineState.IDLE;
        }

        return newState;
    }

    internal async Task onStateChange(LaundryMachineState newState) {
        switch (newState) {
            case LaundryMachineState.ACTIVE:
                logger.LogInformation("Started a load of laundry.");
                await pagerDutyManager.createChange();
                pagerdutyDedupKey = null;
                break;

            case LaundryMachineState.COMPLETE:
                logger.LogInformation("Laundry is finished.");
                pagerdutyDedupKey = await pagerDutyManager.createIncident();
                break;

            case LaundryMachineState.IDLE when pagerdutyDedupKey is not null:
                logger.LogInformation("Laundry is being emptied.");
                await pagerDutyManager.resolveIncident(pagerdutyDedupKey);
                pagerdutyDedupKey = null;
                break;

            default:
                break;
        }
    }

}