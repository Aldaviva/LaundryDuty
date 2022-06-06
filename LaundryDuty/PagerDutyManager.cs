using Pager.Duty;

namespace LaundryDuty;

public interface PagerDutyManager {

    Task<string> createIncident();

    Task resolveIncident(string dedupKey);

    Task createChange();

}

public class PagerDutyManagerImpl: PagerDutyManager {

    private readonly IPagerDuty pagerDuty;

    public PagerDutyManagerImpl(IPagerDuty pagerDuty) {
        this.pagerDuty = pagerDuty;
    }

    public Task createChange() {
        return pagerDuty.Send(new Change("The washing machine is starting a load of laundry."));
    }

    public async Task<string> createIncident() {
        AlertResponse alertResponse = await pagerDuty.Send(new TriggerAlert(Severity.Info, "The washing machine has finished a load of laundry.") {
            Class     = "laundry",
            Component = "washing-machine-00",
            Group     = "garage-00"
        });

        return alertResponse.DedupKey;
    }

    public Task resolveIncident(string dedupKey) {
        return pagerDuty.Send(new ResolveAlert(dedupKey));
    }

}