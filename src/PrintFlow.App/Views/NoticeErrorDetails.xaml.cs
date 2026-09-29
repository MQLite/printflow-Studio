using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;

namespace PrintFlow.App.Views;

/// <summary>The in-place Error details under a failure notice: the exact code, read-only and copyable.</summary>
public partial class NoticeErrorDetails : UserControl
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(NoticeErrorDetails));

    public string? Code
    {
        get => (string?)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public NoticeErrorDetails()
    {
        InitializeComponent();
        SetBinding(CodeProperty, new Binding("NoticeErrorCode"));
    }
}
