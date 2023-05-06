﻿using Pager.Duty;

namespace LaundryDuty;

public interface PagerDutyManager {

    Task<string?> createIncident(Severity severity, string summary, string component);

    Task resolveIncident(string dedupKey);

    Task createChange();

}

public class PagerDutyManagerImpl: PagerDutyManager {

    private readonly IPagerDuty                    pagerDuty;
    private readonly ILogger<PagerDutyManagerImpl> logger;

    public PagerDutyManagerImpl(IPagerDuty pagerDuty, ILogger<PagerDutyManagerImpl> logger) {
        this.pagerDuty = pagerDuty;
        this.logger    = logger;
    }

    public async Task createChange() {
        try {
            await pagerDuty.Send(new Change("The washing machine is starting a load of laundry."));
            logger.LogDebug("Created change event in PagerDuty");
        } catch (PagerDutyException e) {
            logger.LogWarning(e, "Failed to create Change event in PagerDuty");
        }
    }

    public async Task<string?> createIncident(Severity severity, string summary, string component) {
        try {
            AlertResponse alertResponse = await pagerDuty.Send(new TriggerAlert(severity, summary) {
                Class     = "laundry",
                Component = component,
                Group     = "garage-00"
            });

            return alertResponse.DedupKey;
        } catch (PagerDutyException e) {
            logger.LogWarning(e, "Failed to trigger Alert in PagerDuty");
            return null;
        }
    }

    public async Task resolveIncident(string dedupKey) {
        try {
            await pagerDuty.Send(new ResolveAlert(dedupKey));
        } catch (PagerDutyException e) {
            logger.LogWarning(e, "Failed to resolve Alert {dedupKey} in PagerDuty", dedupKey);
        }
    }

}