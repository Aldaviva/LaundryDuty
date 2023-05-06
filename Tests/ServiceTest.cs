using System.Reflection;
using FluentAssertions;
using LaundryDuty;
using Microsoft.Extensions.Hosting;

namespace Tests;

public class ServiceTest: IDisposable {

    private readonly IServiceHostInterceptor hostInterceptor = new ServiceHostInterceptor();

    [Fact]
    public async Task start() {
        hostInterceptor.start();

        MethodInfo mainMethod = typeof(Program).GetMethod("<Main>$", BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(string[]) })!;

        Task mainTask = (Task) mainMethod.Invoke(null, new object[] { new[] { "Environment=Test" } })!;

        hostInterceptor.host?.StopAsync();

        await mainTask;
    }

    [Fact]
    public void serviceName() {
        WindowsServiceLifetimeOptions serviceLifetimeOptions = new();
        WindowsService.configureWindowsService(serviceLifetimeOptions);
        serviceLifetimeOptions.ServiceName.Should().Be("LaundryDuty");
    }

    public void Dispose() {
        hostInterceptor.Dispose();
    }

}