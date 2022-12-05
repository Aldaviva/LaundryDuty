using FakeItEasy;
using FluentAssertions;
using LaundryDuty;
using Microsoft.Extensions.Logging.Abstractions;
using Pager.Duty;

namespace Tests;

public class PagerDutyManagerTest {

    private readonly IPagerDuty           pagerDuty = A.Fake<IPagerDuty>();
    private readonly PagerDutyManagerImpl pagerDutyManager;

    public PagerDutyManagerTest() {
        pagerDutyManager = new PagerDutyManagerImpl(pagerDuty, new NullLogger<PagerDutyManagerImpl>());
    }

    [Fact]
    public async Task createChange() {
        await pagerDutyManager.createChange();

        A.CallTo(() => pagerDuty.Send(A<Change>.That.Matches(actual => actual.Summary == "The washing machine is starting a load of laundry."))).MustHaveHappened();
    }

    [Fact]
    public async Task createIncident() {
        A.CallTo(() => pagerDuty.Send(A<TriggerAlert>._)).Returns(new AlertResponse { DedupKey = "abc" });

        string? actualDedupKey = await pagerDutyManager.createIncident();

        A.CallTo(() => pagerDuty.Send(A<TriggerAlert>.That.Matches(actual =>
            actual.Summary == "The washing machine has finished a load of laundry." &&
            actual.Severity == Severity.Info &&
            actual.Class == "laundry" &&
            actual.Component == "washing-machine-00" &&
            actual.Group == "garage-00")
        )).MustHaveHappened();

        actualDedupKey.Should().Be("abc");
    }

    [Fact]
    public async Task resolveIncident() {
        await pagerDutyManager.resolveIncident("abc");

        A.CallTo(() => pagerDuty.Send(A<ResolveAlert>.That.Matches(actual => actual.DedupKey == "abc"))).MustHaveHappened();
    }

    [Fact]
    public async Task ignoreCreateChangeFailures() {
        A.CallTo(() => pagerDuty.Send(A<Change>._)).ThrowsAsync(new NetworkException("test failure", null));

        await pagerDutyManager.createChange();
    }

    [Fact]
    public async Task ignoreTriggerAlertFailures() {
        A.CallTo(() => pagerDuty.Send(A<TriggerAlert>._)).ThrowsAsync(new NetworkException("test failure", null));

        string? actualDedupKey = await pagerDutyManager.createIncident();
        actualDedupKey.Should().BeNull();
    }

    [Fact]
    public async Task ignoreResolveAlertFailures() {
        A.CallTo(() => pagerDuty.Send(A<ResolveAlert>._)).ThrowsAsync(new NetworkException("test failure", null));

        await pagerDutyManager.resolveIncident("abc");
    }

}