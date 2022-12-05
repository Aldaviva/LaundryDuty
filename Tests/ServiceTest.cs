using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using LaundryDuty;
using Microsoft.Extensions.Hosting;

namespace Tests;

public class ServiceTest: IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>> {

    private IHost? host;

    [Fact]
    public async Task start() {
        DiagnosticListener.AllListeners.Subscribe(this);

        MethodInfo mainMethod = typeof(Program).GetMethod("<Main>$", BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(string[]) })!;

        Task mainTask = (Task) mainMethod.Invoke(null, new object[] { new[] { "Environment=Test" } })!;

        host?.StopAsync();

        await mainTask;
    }

    [Fact]
    public void serviceName() {
        WindowsServiceLifetimeOptions serviceLifetimeOptions = new();
        WindowsService.configureWindowsService(serviceLifetimeOptions);
        serviceLifetimeOptions.ServiceName.Should().Be("LaundryDuty");
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void OnNext(DiagnosticListener listener) {
        if (listener.Name == "Microsoft.Extensions.Hosting") {
            listener.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> diagnosticEvent) {
        switch (diagnosticEvent.Key) {
            case "HostBuilding":
                // HostBuilder hostBuilder = (HostBuilder) diagnosticEvent.Value!;
                // you can add, modify, and remove registered services here
                break;
            case "HostBuilt":
                host = diagnosticEvent.Value as IHost;
                break;
        }
    }

}