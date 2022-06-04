﻿namespace LaundryDuty;

public class Configuration {

    public int minimumActiveMilliwatts { get; set; }
    public int maximumIdleMilliwatts { get; set; }
    public int pollingIntervalMilliseconds { get; set; }
    public string pagerDutyRoutingKey { get; set; } = null!;
    public string outletHostname { get; set; } = null!;

}