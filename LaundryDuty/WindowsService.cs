namespace LaundryDuty;

public readonly struct WindowsService {

    public static void configureWindowsService(WindowsServiceLifetimeOptions options) {
        options.ServiceName = "LaundryDuty";
    }

}