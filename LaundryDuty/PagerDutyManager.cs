using PagerDuty.Events;

namespace LaundryDuty;

public interface PagerDutyManager {

    string createIncident();

    void resolveIncident(string dedupKey);

}

public class PagerDutyManagerImpl: PagerDutyManager {

    public PagerDutyManagerImpl(Configuration config) {
        Environment.SetEnvironmentVariable("ROUTING_KEY", config.pagerDutyRoutingKey);
    }

    public string createIncident() {
        TriggerEvent request = new();
        request.SetSeverity(EventSeverity.Info);
        request.SetSummary("The washing machine has finished a load of laundry.");
        request.SetComponent("washing-machine-00");
        request.SetGroup("garage-00");
        request.SetClass("laundry");

        return Pager.EnqueueEvent(request).DedupKey;
    }

    public void resolveIncident(string dedupKey) {
        Pager.EnqueueEvent(new ResolveEvent { DedupKey = dedupKey });
    }

}

internal class ResolveEvent: Event {

    public ResolveEvent() {
        Action = EventAction.Resolve.ToString().ToLower();
    }

}