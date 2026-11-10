using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Io;

public partial class IoManagementViewModel : ViewModelBase, IDisposable
{
    private const int TopInterruptCount = 8;

    private readonly IoService _ioService = new();

    // Ticks on the UI thread; only runs while the page is on screen.
    private readonly DispatcherTimer _timer;

    // The previous reading, needed to turn cumulative counters into rates.
    private IoSnapshot? _previous;

    private bool _isDisposed;

    public ObservableCollection<DiskIoModel> Disks { get; } = new();

    public ObservableCollection<NetworkInterfaceModel> Interfaces { get; } = new();

    // Keyboards, mice and touchpads.
    public ObservableCollection<InputDeviceModel> MainInputDevices { get; } = new();

    // System buttons, hotkeys, audio-jack sensors and similar event sources.
    public ObservableCollection<InputDeviceModel> OtherInputDevices { get; } = new();

    public ObservableCollection<InterruptSourceModel> TopInterrupts { get; } = new();

    public ObservableCollection<RegisteredDriverModel> CharacterDrivers { get; } = new();

    public ObservableCollection<RegisteredDriverModel> BlockDrivers { get; } = new();

    [ObservableProperty]
    private string diskReadRate = "-";

    [ObservableProperty]
    private string diskWriteRate = "-";

    [ObservableProperty]
    private string networkDownloadRate = "-";

    [ObservableProperty]
    private string networkUploadRate = "-";

    [ObservableProperty]
    private string interruptRate = "-";

    [ObservableProperty]
    private string contextSwitchRate = "-";

    [ObservableProperty]
    private string interruptTotalText = string.Empty;

    [ObservableProperty]
    private bool isMeasuring = true;

    [ObservableProperty]
    private string lastUpdated = "Not sampled yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotices))]
    private string notices = string.Empty;

    [ObservableProperty]
    private string characterDriversTitle = string.Empty;

    [ObservableProperty]
    private string blockDriversTitle = string.Empty;

    [ObservableProperty]
    private string otherInputDevicesTitle = string.Empty;

    public bool HasNotices => !string.IsNullOrEmpty(Notices);

    public bool HasNoDisks => Disks.Count == 0;

    public bool HasNoInterfaces => Interfaces.Count == 0;

    public bool HasOtherInputDevices => OtherInputDevices.Count > 0;

    public bool HasNoInputDevices => MainInputDevices.Count == 0 && OtherInputDevices.Count == 0;

    public bool HasNoInterrupts => TopInterrupts.Count == 0;

    public IoManagementViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Called by the view when it appears. Takes a baseline reading, then samples every 2 seconds.</summary>
    public void StartMonitoring()
    {
        if (_isDisposed || _timer.IsEnabled)
            return;

        // Anything older than "now" is stale after time away from the page.
        _previous = null;
        IsMeasuring = true;
        _ioService.ResetCache();

        LoadSlowChangingInformation();
        Sample();

        _timer.Start();
    }

    /// <summary>Called by the view when it disappears, so nothing keeps sampling in the background.</summary>
    public void StopMonitoring()
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Sample();
    }

    // Input devices and drivers rarely change, so they are read once per visit.
    private void LoadSlowChangingInformation()
    {
        MainInputDevices.Clear();
        OtherInputDevices.Clear();

        foreach (InputDeviceModel device in _ioService.ReadInputDevices())
        {
            if (device.IsPrimary)
                MainInputDevices.Add(device);
            else
                OtherInputDevices.Add(device);
        }

        OtherInputDevicesTitle =
            $"System buttons, hotkeys and audio sensors ({OtherInputDevices.Count}) - not typed by a person";

        var (character, block) = _ioService.ReadDrivers();

        CharacterDrivers.Clear();
        BlockDrivers.Clear();

        foreach (RegisteredDriverModel driver in character)
            CharacterDrivers.Add(driver);

        foreach (RegisteredDriverModel driver in block)
            BlockDrivers.Add(driver);

        CharacterDriversTitle = $"Character device drivers ({CharacterDrivers.Count})";
        BlockDriversTitle = $"Block device drivers ({BlockDrivers.Count})";

        OnPropertyChanged(nameof(HasNoInputDevices));
        OnPropertyChanged(nameof(HasOtherInputDevices));
    }

    private void Sample()
    {
        if (_isDisposed)
            return;

        IoSnapshot current = _ioService.ReadSnapshot();
        double? seconds = IoRateCalculator.IntervalSeconds(_previous, current);

        Disks.Clear();

        foreach (DiskCounters disk in current.Disks.Values.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            DiskCounters? before = null;
            _previous?.Disks.TryGetValue(disk.Name, out before);

            Disks.Add(IoRateCalculator.BuildDisk(disk, before, seconds, _ioService.GetDiskDetails(disk.Name)));
        }

        Interfaces.Clear();

        foreach (NetworkCounters network in current.Interfaces.Values.OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            NetworkCounters? before = null;
            _previous?.Interfaces.TryGetValue(network.Name, out before);

            Interfaces.Add(IoRateCalculator.BuildNetwork(network, before, seconds));
        }

        UpdateSummary(current, seconds);

        TopInterrupts.Clear();

        foreach (InterruptSourceModel source in _ioService.ReadTopInterrupts(TopInterruptCount))
            TopInterrupts.Add(source);

        Notices = string.Join("  ", current.Warnings.Distinct());
        IsMeasuring = seconds is null;
        LastUpdated = $"Last updated {DateTime.Now:HH:mm:ss} (refreshes every 2 seconds)";

        OnPropertyChanged(nameof(HasNoDisks));
        OnPropertyChanged(nameof(HasNoInterfaces));
        OnPropertyChanged(nameof(HasNoInterrupts));

        _previous = current;
    }

    private void UpdateSummary(IoSnapshot current, double? seconds)
    {
        // Sum only values that exist; "-" when nothing could be measured.
        DiskReadRate = SumRate(Disks.Select(d => d.ReadBytesPerSecond));
        DiskWriteRate = SumRate(Disks.Select(d => d.WriteBytesPerSecond));

        NetworkInterfaceModel[] real = Interfaces.Where(i => i.CountsTowardsSummary).ToArray();
        NetworkDownloadRate = SumRate(real.Select(i => i.RxBytesPerSecond));
        NetworkUploadRate = SumRate(real.Select(i => i.TxBytesPerSecond));

        double? interrupts = _previous?.TotalInterrupts is long before && current.TotalInterrupts is long now
            ? IoRateCalculator.RatePerSecond(before, now, seconds)
            : null;

        double? switches = _previous?.ContextSwitches is long switchesBefore && current.ContextSwitches is long switchesNow
            ? IoRateCalculator.RatePerSecond(switchesBefore, switchesNow, seconds)
            : null;

        InterruptRate = IoRateCalculator.FormatCountPerSecond(interrupts);
        ContextSwitchRate = IoRateCalculator.FormatCountPerSecond(switches);

        InterruptTotalText = current.TotalInterrupts is long total
            ? $"{total:N0} interrupts handled since boot"
            : "Interrupt total unavailable";
    }

    private static string SumRate(System.Collections.Generic.IEnumerable<double?> rates)
    {
        double? total = null;

        foreach (double? rate in rates)
        {
            if (rate is not null)
                total = (total ?? 0) + rate.Value;
        }

        return IoRateCalculator.FormatBytesPerSecond(total);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
