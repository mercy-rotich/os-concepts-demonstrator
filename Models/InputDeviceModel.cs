namespace OS_Concepts_Demonstrator.Models;

public class InputDeviceModel
{
    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    // Where it is attached: USB, PS/2, Bluetooth, ...
    public string Connection { get; set; } = string.Empty;

    // The kernel handlers (drivers) serving it, e.g. "kbd event3".
    public string Handlers { get; set; } = string.Empty;

    // Real user-facing devices (keyboard, mouse, touchpad...) as opposed to system event sources.
    public bool IsPrimary { get; set; }

    public string Summary => $"{Category}  |  {Connection}";

    public string HandlersText => $"Kernel handlers: {Handlers}";

    public string Subtitle => $"{Category}  |  {Connection}  |  handlers: {Handlers}";
}
