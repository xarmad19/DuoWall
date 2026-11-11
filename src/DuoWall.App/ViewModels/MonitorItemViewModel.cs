using CommunityToolkit.Mvvm.ComponentModel;
using DuoWall.Core.Models;

namespace DuoWall.App.ViewModels;

public sealed partial class MonitorItemViewModel : ObservableObject
{
    public MonitorInfo Model { get; }
    private readonly Action _onCalibrationChanged;

    [ObservableProperty] private double _userOffsetMmY;
    [ObservableProperty] private double _bezelLeftMm;
    [ObservableProperty] private double _bezelRightMm;
    [ObservableProperty] private bool _isIncluded;

    public string Id => Model.Id;
    public string DisplayLabel => Model.DisplayLabel;
    public string DeviceName => Model.DeviceName;
    public string DeviceId => Model.DeviceId;
    public string Manufacturer => string.IsNullOrWhiteSpace(Model.Manufacturer) ? "Standard Display" : Model.Manufacturer;
    public string ResolutionLabel => Model.ResolutionLabel;
    public string AspectRatioLabel => Model.AspectRatioLabel;
    public string PhysicalSizeLabel => Model.PhysicalSizeLabel;
    public string VirtualPositionLabel => $"X: {Model.Bounds.X}, Y: {Model.Bounds.Y}";
    public string WorkingAreaLabel => $"{Model.WorkingArea.Width} × {Model.WorkingArea.Height} (at {Model.WorkingArea.X}, {Model.WorkingArea.Y})";
    public string DpiScaleLabel => $"{Model.ScalePercent:0}% ({Model.DpiX} DPI)";
    public string RefreshRateLabel => $"{Model.RefreshRateHz} Hz";
    public string OrientationLabel => Model.Orientation.ToString();
    public string PrimaryBadge => Model.IsPrimary ? "Primary Display" : "Secondary Display";
    public bool IsPrimary => Model.IsPrimary;
    public string PpiLabel => $"{Model.EffectivePpiX:0.0} PPI";
    public string EdidSourceLabel => Model.HasHardwarePhysicalDimensions ? "Hardware EDID" : "DPI Estimated";

    public MonitorItemViewModel(MonitorInfo model, Action onCalibrationChanged)
    {
        Model = model;
        _onCalibrationChanged = onCalibrationChanged;
        _userOffsetMmY = model.UserOffsetMmY;
        _bezelLeftMm = model.BezelLeftMm;
        _bezelRightMm = model.BezelRightMm;
        _isIncluded = model.IsIncluded;
    }

    partial void OnUserOffsetMmYChanged(double value)
    {
        Model.UserOffsetMmY = value;
        _onCalibrationChanged();
    }

    partial void OnBezelLeftMmChanged(double value)
    {
        Model.BezelLeftMm = value;
        _onCalibrationChanged();
    }

    partial void OnBezelRightMmChanged(double value)
    {
        Model.BezelRightMm = value;
        _onCalibrationChanged();
    }

    partial void OnIsIncludedChanged(bool value)
    {
        Model.IsIncluded = value;
        _onCalibrationChanged();
    }

    public void SyncFromModel()
    {
        UserOffsetMmY = Model.UserOffsetMmY;
        BezelLeftMm = Model.BezelLeftMm;
        BezelRightMm = Model.BezelRightMm;
        IsIncluded = Model.IsIncluded;
    }
}
