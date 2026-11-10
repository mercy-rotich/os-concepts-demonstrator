namespace OS_Concepts_Demonstrator.Models;

/// <summary>An entry of /proc/devices: a driver registered under a major device number.</summary>
public class RegisteredDriverModel
{
    public int Major { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Text => $"{Major}  {Name}";
}
