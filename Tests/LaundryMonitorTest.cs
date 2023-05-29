using System.Net.Sockets;
using FakeItEasy;
using FluentAssertions;
using Kasa;
using LaundryDuty;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pager.Duty;
using NetworkException = Kasa.NetworkException;

namespace Tests;

public class LaundryMonitorTest: IDisposable {

    private readonly Configuration configuration = new() {
        minimumActiveMilliwatts     = 750,
        maximumIdleMilliwatts       = 413,
        pollingIntervalMilliseconds = 10,
        outletHostname              = "192.168.1.100"
    };

    private readonly LaundryMonitor           laundryMonitor;
    private readonly PagerDutyManager         pagerDutyManager = A.Fake<PagerDutyManager>();
    private readonly IKasaOutlet              outlet           = A.Fake<IKasaOutlet>();
    private readonly IHostApplicationLifetime hostLifetime     = A.Fake<IHostApplicationLifetime>();

    public LaundryMonitorTest() {
        laundryMonitor = new LaundryMonitor(A.Fake<ILogger<LaundryMonitor>>(), outlet, pagerDutyManager, configuration, hostLifetime);
    }

    public static TheoryData<int, LaundryMachineState?, LaundryMachineState> stateTransitions => new() {
        // initial to active
        { 1000, null, LaundryMachineState.ACTIVE },

        // stay in active
        { 1000, LaundryMachineState.ACTIVE, LaundryMachineState.ACTIVE },

        // initial to idle
        { 380, null, LaundryMachineState.IDLE },

        // stay in idle
        { 380, LaundryMachineState.IDLE, LaundryMachineState.IDLE },

        // active to idle
        { 380, LaundryMachineState.ACTIVE, LaundryMachineState.IDLE },

        // complete to idle
        { 380, LaundryMachineState.COMPLETE, LaundryMachineState.IDLE },

        // ignore zeros
        { 0, LaundryMachineState.IDLE, LaundryMachineState.IDLE },
        { 0, LaundryMachineState.COMPLETE, LaundryMachineState.COMPLETE },

        // idle to active
        { 1000, LaundryMachineState.IDLE, LaundryMachineState.ACTIVE },

        // active to complete
        { 440, LaundryMachineState.ACTIVE, LaundryMachineState.COMPLETE },

        // complete to active
        { 1000, LaundryMachineState.COMPLETE, LaundryMachineState.ACTIVE },

        // stay in old state when power is in the gray area
        { 440, LaundryMachineState.COMPLETE, LaundryMachineState.COMPLETE },
        { 440, LaundryMachineState.IDLE, LaundryMachineState.IDLE },
    };

    [Theory] [MemberData(nameof(stateTransitions))]
    internal void stateTransition(int powerMilliwatts, LaundryMachineState? oldState, LaundryMachineState expected) {
        LaundryMonitor.getNewState(powerMilliwatts, oldState, configuration).Should().Be(expected);
    }

    [Fact]
    public async Task sendChangeWhenStarting() {
        await laundryMonitor.onStateChange(LaundryMachineState.ACTIVE);

        A.CallTo(() => pagerDutyManager.createChange()).MustHaveHappened();
    }

    [Fact]
    public async Task triggerAlertWhenCompleting() {
        laundryMonitor.pagerDutyLaundryDoneDedupKey.Should().BeNull();
        A.CallTo(() => pagerDutyManager.createIncident(Severity.Info, "The washing machine has finished a load of laundry.", "washing-machine-00")).Returns("abc");

        await laundryMonitor.onStateChange(LaundryMachineState.COMPLETE);

        A.CallTo(() => pagerDutyManager.createIncident(Severity.Info, "The washing machine has finished a load of laundry.", "washing-machine-00")).MustHaveHappened();
        laundryMonitor.pagerDutyLaundryDoneDedupKey.Should().Be("abc");
    }

    [Fact]
    public async Task resolveAlertWhenOpening() {
        laundryMonitor.pagerDutyLaundryDoneDedupKey = "abc";

        await laundryMonitor.onStateChange(LaundryMachineState.IDLE);

        A.CallTo(() => pagerDutyManager.resolveIncident("abc")).MustHaveHappened();
        laundryMonitor.pagerDutyLaundryDoneDedupKey.Should().BeNull();
    }

    [Fact]
    public async Task skipResolvingAlertOnBoot() {
        laundryMonitor.pagerDutyLaundryDoneDedupKey = null;

        await laundryMonitor.onStateChange(LaundryMachineState.IDLE);

        A.CallTo(() => pagerDutyManager.resolveIncident(A<string>._)).MustNotHaveHappened();
        laundryMonitor.pagerDutyLaundryDoneDedupKey.Should().BeNull();
    }

    [Fact]
    public async Task oneIteration() {
        laundryMonitor.state = LaundryMachineState.IDLE;
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).Returns(new PowerUsage(7466, 118011, 749146, 0));

        await laundryMonitor.executeOnce();

        A.CallTo(() => pagerDutyManager.createChange()).MustHaveHappened();
        laundryMonitor.state.Should().Be(LaundryMachineState.ACTIVE);
    }

    [Fact]
    public async Task loop() {
        const int MIN_EXPECTED_INVOCATIONS = 3;

        using CountdownEvent latch = new(MIN_EXPECTED_INVOCATIONS);

        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ReturnsLazily(() => {
            latch.Signal();
            return new PowerUsage(10, 120000, 0, 0);
        });

        await laundryMonitor.StartAsync(CancellationToken.None);

        latch.Wait(TimeSpan.FromSeconds(30));

        await laundryMonitor.StopAsync(CancellationToken.None);

        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).MustHaveHappenedANumberOfTimesMatching(i => i >= MIN_EXPECTED_INVOCATIONS);
    }

    [Fact]
    public async Task ignoreTcpErrors() {
        laundryMonitor.state = LaundryMachineState.IDLE;
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ThrowsAsync(new NetworkException("The TCP socket failed to connect", "192.168.1.100", new SocketException(11011)));

        await laundryMonitor.executeOnce();

        laundryMonitor.state.Should().Be(LaundryMachineState.IDLE);
    }

    [Fact]
    public async Task ignoreJsonErrors() {
        laundryMonitor.state = LaundryMachineState.IDLE;
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ThrowsAsync(new ResponseParsingException("", "", typeof(string), "", new IOException()));

        await laundryMonitor.executeOnce();

        laundryMonitor.state.Should().Be(LaundryMachineState.IDLE);
    }

    [Fact]
    public async Task throwFeatureUnavailable() {
        laundryMonitor.state = LaundryMachineState.IDLE;
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ThrowsAsync(new FeatureUnavailable("", Feature.EnergyMeter, ""));

        Func<Task> thrower = () => laundryMonitor.executeOnce();
        await thrower.Should().ThrowAsync<FeatureUnavailable>();

    }

    [Fact]
    public async Task crashOnFeatureUnavailable() {
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ThrowsAsync(new FeatureUnavailable("", Feature.EnergyMeter, ""));

        await laundryMonitor.StartAsync(CancellationToken.None);

        Environment.ExitCode.Should().Be(1);
        A.CallTo(() => hostLifetime.StopApplication()).MustHaveHappened();
    }

    [Fact]
    public async Task crashOnUnhandledException() {
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ThrowsAsync(new Exception("please crash"));

        await laundryMonitor.StartAsync(CancellationToken.None);

        Environment.ExitCode.Should().Be(1);
        A.CallTo(() => hostLifetime.StopApplication()).MustHaveHappened();
    }

    [Fact]
    public async Task exitGracefullyOnCancel() {
        await laundryMonitor.StartAsync(new CancellationToken(true));

        Environment.ExitCode.Should().Be(0);
        A.CallTo(() => hostLifetime.StopApplication()).MustNotHaveHappened();
    }

    [Fact]
    public async Task triggerIncidentWhenOutletOffline() {
        Thread.Sleep(50); // I don't know why this makes the test stop failing, they're not parallel in the same class anyway, and there's no shared or static state

        laundryMonitor.pagerDutyOutletOfflineDedupKey.Should().BeNull();
        configuration.outletOfflineDurationBeforeIncidentMilliseconds = 1;
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ThrowsAsync(new NetworkException("The TCP socket failed to connect", "192.168.1.100", new SocketException(11011)));
        A.CallTo(() => pagerDutyManager.createIncident(Severity.Error, A<string>._, A<string>._)).Returns("def");

        await laundryMonitor.executeOnce();
        await laundryMonitor.executeOnce();

        laundryMonitor.pagerDutyOutletOfflineDedupKey.Should().NotBeNull();
        A.CallTo(() => pagerDutyManager.createIncident(Severity.Error, "The washing machine's Kasa smart outlet has been unreachable for at least 0:00:00.001", "192.168.1.100"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task resolveIncidentWhenOutletBackOnline() {
        Thread.Sleep(50); // I don't know why this makes the test stop failing, they're not parallel in the same class anyway, and there's no shared or static state
        laundryMonitor.pagerDutyOutletOfflineDedupKey.Should().BeNull();
        configuration.outletOfflineDurationBeforeIncidentMilliseconds = 1;
        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).ThrowsAsync(new NetworkException("The TCP socket failed to connect", "192.168.1.100", new SocketException(11011)));
        A.CallTo(() => pagerDutyManager.createIncident(Severity.Error, A<string>._, A<string>._)).Returns("ghi");

        laundryMonitor.pagerDutyOutletOfflineDedupKey.Should().BeNull();
        await laundryMonitor.executeOnce(); // failure
        A.CallTo(() => pagerDutyManager.createIncident(Severity.Error, "The washing machine's Kasa smart outlet has been unreachable for at least 0:00:00.001", "192.168.1.100"))
            .MustHaveHappenedOnceExactly();
        laundryMonitor.pagerDutyOutletOfflineDedupKey.Should().NotBeNull();

        A.CallTo(() => outlet.EnergyMeter.GetInstantaneousPowerUsage()).Returns(new PowerUsage(0, 0, 0, 0));

        await laundryMonitor.executeOnce(); // success

        A.CallTo(() => pagerDutyManager.resolveIncident("ghi")).MustHaveHappened();
        laundryMonitor.pagerDutyOutletOfflineDedupKey.Should().BeNull();
    }

    public void Dispose() {
        Environment.ExitCode = 0;
    }

}