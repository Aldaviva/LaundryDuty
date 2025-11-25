using System.Diagnostics.CodeAnalysis;

namespace LaundryDuty;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")] // all properties need to be settable for tests to work, and public for DI config reading to work
public class Configuration {

    public int minimumActiveMilliwatts { get; set; } = 750;
    public int maximumIdleMilliwatts { get; set; } = 413;
    public int pollingIntervalMilliseconds { get; set; } = 15_000;
    public string pagerDutyIntegrationKey { get; set; } = null!;
    public string outletHostname { get; set; } = null!;
    public int outletTimeoutMilliseconds { get; set; } = 2000;
    public uint outletMaxAttempts { get; set; } = 10;
    public int outletRetryDelayMilliseconds { get; set; } = 1000;
    public ulong outletOfflineDurationBeforeIncidentMilliseconds { get; set; } = 0;

}