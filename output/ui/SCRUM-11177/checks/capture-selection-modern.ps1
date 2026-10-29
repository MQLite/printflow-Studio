# Run only in a new disposable pwsh -NoProfile -STA process.
# Diagnostic alternate renderer; this never launches App or changes repository runtime settings.
$ErrorActionPreference = 'Stop'
[AppContext]::SetSwitch('Switch.System.Windows.Controls.Text.UseAdornerForTextboxSelectionRendering', $false)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$evidenceDir = Split-Path $PSScriptRoot
$tokensPath = Join-Path $repoRoot 'src/PrintFlow.App/Themes/GuidedTokens.xaml'
$controlsPath = Join-Path $repoRoot 'src/PrintFlow.App/Themes/GuidedControls.xaml'
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
foreach ($active in @($true, $false)) {
    $resources = [Windows.ResourceDictionary]::new()
    $resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($tokensPath)))
    $resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($controlsPath)))
    $root = [Windows.Controls.Border]::new()
    $root.Resources = $resources
    $root.Background = [Windows.Media.Brushes]::White
    $root.Padding = [Windows.Thickness]::new(20)
    $inputBox = [Windows.Controls.TextBox]::new()
    $inputBox.Text = '客户输出名称 selected output name'
    $inputBox.IsInactiveSelectionHighlightEnabled = $true
    $inputBox.SetResourceReference([Windows.FrameworkElement]::StyleProperty, 'PF.Input.TextBox')
    $root.Child = $inputBox
    $root.Measure([Windows.Size]::new(500, 100))
    $root.Arrange([Windows.Rect]::new(0, 0, 500, 100))
    $root.UpdateLayout()
    $focusKey = [Windows.UIElement].GetField('IsKeyboardFocusedPropertyKey', $flags).GetValue($null)
    $selectionKey = [Windows.Controls.Primitives.TextBoxBase].GetField('IsSelectionActivePropertyKey', $flags).GetValue($null)
    $inputBox.SetValue($focusKey, $active)
    $inputBox.SelectAll()
    $inputBox.SetValue($selectionKey, $active)
    $selection = [Windows.Controls.Primitives.TextBoxBase].GetProperty('TextSelectionInternal', $flags).GetValue($inputBox)
    $null = $selection.GetType().GetMethod('Highlight', $flags).Invoke($selection, @())
    $root.UpdateLayout()
    [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::SystemIdle)
    $root.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(500, 100, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($root)
    $pixels = [byte[]]::new(500 * 100 * 4)
    $bitmap.CopyPixels($pixels, 500 * 4, 0)
    $inkPixels = 0
    $selectionPixels = 0
    for ($i = 0; $i -lt $pixels.Length; $i += 4) {
        if ([Math]::Abs($pixels[$i] - 0x2A) -le 20 -and [Math]::Abs($pixels[$i+1] - 0x2E) -le 20 -and [Math]::Abs($pixels[$i+2] - 0x20) -le 20 -and $pixels[$i+3] -eq 255) { $inkPixels++ }
        if ($pixels[$i] -eq 0xEF -and $pixels[$i+1] -eq 0xF4 -and $pixels[$i+2] -eq 0xE8 -and $pixels[$i+3] -eq 255) { $selectionPixels++ }
    }
    if ($inputBox.SelectionTextBrush.ToString() -ne '#FF202E2A' -or $inkPixels -lt 100 -or $selectionPixels -lt 100) { throw 'Selection contrast diagnostic failed.' }
    $state = if ($active) { 'active' } else { 'inactive' }
    $stem = "selection-$state-modern-after"
    $pngPath = Join-Path $evidenceDir "$stem.png"
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($pngPath)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    [ordered]@{
        State = $stem; WidthDip = 500; HeightDip = 100; Dpi = 96
        Method = 'Isolated process; explicitly selected non-adorner WPF renderer; synthetic focus/selection flags; no App, Window.Show or desktop input'
        UseAdornerForTextboxSelectionRendering = $false
        IsSelectionActive = $inputBox.IsSelectionActive; SelectedCharacters = $inputBox.SelectionLength
        SelectionTextBrush = $inputBox.SelectionTextBrush.ToString(); SelectionBrush = $inputBox.SelectionBrush.ToString()
        InkPixels = $inkPixels; SelectionPixels = $selectionPixels
        ScreenshotSha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash
        TokensSha256 = (Get-FileHash -LiteralPath $tokensPath -Algorithm SHA256).Hash
        ControlsSha256 = (Get-FileHash -LiteralPath $controlsPath -Algorithm SHA256).Hash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDir "$stem.json") -Encoding utf8
    Write-Output "$stem PASS: ink=$inkPixels selection=$selectionPixels"
}
