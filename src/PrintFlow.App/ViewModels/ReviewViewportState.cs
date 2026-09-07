using CommunityToolkit.Mvvm.ComponentModel;

namespace PrintFlow.App.ViewModels;

public enum ReviewComparisonMode { SideBySide, Slider }
public enum ReviewInspectionBackground { Checkerboard, White, Black }

/// <summary>Ephemeral presentation preferences; no workflow authority or persisted data.</summary>
public sealed partial class ReviewViewportState : ObservableObject
{
    [ObservableProperty] private ReviewComparisonMode _mode;
    [ObservableProperty] private ReviewInspectionBackground _background;
    [ObservableProperty] private double _sliderPosition = 50;
    [ObservableProperty] private double _horizontalPosition = 0.5;
    [ObservableProperty] private double _verticalPosition = 0.5;

    public void Reset()
    {
        HorizontalPosition = 0.5;
        VerticalPosition = 0.5;
    }
}
