using Kasa;
using Pager.Duty;
using NetworkException = Kasa.NetworkException;

namespace LaundryDuty;

public class LaundryMonitor: BackgroundService {

    private readonly ILogger<LaundryMonitor>  logger;
    private readonly IKasaOutlet              outlet;
    private readonly PagerDutyManager         pagerDutyManager;
    private readonly Configuration            config;
    private readonly IHostApplicationLifetime hostLifetime;

    internal LaundryMachineState? state;
    internal string?              pagerDutyLaundryDoneDedupKey;
    internal string?              pagerDutyOutletOfflineDedupKey;
    private  DateTime             mostRecentSuccessfulOutletPoll = DateTime.Now;

    public LaundryMonitor(ILogger<LaundryMonitor> logger, IKasaOutlet outlet, PagerDutyManager pagerDutyManager, Configuration config, IHostApplicationLifetime hostLifetime) {
        this.logger           = logger;
        this.outlet           = outlet;
        this.pagerDutyManager = pagerDutyManager;
        this.config           = config;
        this.hostLifetime     = hostLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                await executeOnce();
                await Task.Delay(config.pollingIntervalMilliseconds, stoppingToken);
            }
        } catch (TaskCanceledException) {
            //exit normally
        } catch (FeatureUnavailable) {
            Environment.ExitCode = 1;
            hostLifetime.StopApplication();
        } catch (Exception e) {
            logger.LogError(e, "{message}", e.Message);
            Environment.ExitCode = 1;
            hostLifetime.StopApplication();
        }
    }

    /// <exception cref="FeatureUnavailable">If the configured smart outlet does not have an energy monitor.</exception>
    /// <exception cref="OverflowException">if outletOfflineDurationBeforeIncidentMilliseconds is larger than double.maxValue</exception>
    internal async Task executeOnce() {
        try {
            int powerMilliwatts = (await outlet.EnergyMeter.GetInstantaneousPowerUsage()).Power;

            LaundryMachineState newState = getNewState(powerMilliwatts, state, config);

            if (state != null && state != newState) {
                await onStateChange(newState);
            }

            state = newState;
            logger.LogDebug("Laundry machine is {state}, using {power:N0} mW", state, powerMilliwatts);

            mostRecentSuccessfulOutletPoll = DateTime.Now;
            if (pagerDutyOutletOfflineDedupKey is { } dedupKey) {
                pagerDutyOutletOfflineDedupKey = null;
                await pagerDutyManager.resolveIncident(dedupKey);
            }
        } catch (NetworkException e) {
            logger.LogWarning(e, "Smart outlet {host} is not reachable", e.Hostname);
        } catch (ResponseParsingException e) {
            logger.LogWarning(e, "Failed to parse the {rawResponse} response to a {request} request from smart outlet {host} into {type}: {message}", e.Response, e.RequestMethod, e.Hostname,
                e.ResponseType.FullName, e.Message);
        } catch (FeatureUnavailable e) {
            if (e.RequiredFeature == Feature.EnergyMeter) {
                logger.LogError(e, "Kasa outlet {host} is not a model that has a power meter; models such as EP25, KP125, and KP115 have built-in energy monitoring", e.Hostname);
            }

            throw;
        }

        TimeSpan offlineDurationLimit = TimeSpan.FromMilliseconds(config.outletOfflineDurationBeforeIncidentMilliseconds);
        TimeSpan offlineDuration      = DateTime.Now - mostRecentSuccessfulOutletPoll;
        if (offlineDurationLimit > TimeSpan.Zero && offlineDuration > offlineDurationLimit && pagerDutyOutletOfflineDedupKey is null) {
            pagerDutyOutletOfflineDedupKey = await pagerDutyManager.createIncident(Severity.Error,
                $"The washing machine's Kasa smart outlet has been unreachable for at least {offlineDurationLimit:g}",
                config.outletHostname);
        }
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
                logger.LogInformation("Started a load of laundry");
                await pagerDutyManager.createChange();
                pagerDutyLaundryDoneDedupKey = null;
                break;

            case LaundryMachineState.COMPLETE:
                logger.LogInformation("Laundry is finished");
                pagerDutyLaundryDoneDedupKey = await pagerDutyManager.createIncident(Severity.Info, "The washing machine has finished a load of laundry.", "washing-machine-00");
                break;

            case LaundryMachineState.IDLE when pagerDutyLaundryDoneDedupKey is not null:
                logger.LogInformation("Laundry is being emptied");
                await pagerDutyManager.resolveIncident(pagerDutyLaundryDoneDedupKey);
                pagerDutyLaundryDoneDedupKey = null;
                break;

            default:
                break;
        }
    }

}