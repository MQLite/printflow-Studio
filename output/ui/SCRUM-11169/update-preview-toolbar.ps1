$path='C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/src/PrintFlow.App/Views/SessionScreenView.xaml'
$s=[IO.File]::ReadAllText($path)
$s=[regex]::Replace($s,'(?s)<DockPanel DockPanel.Dock="Top" Margin="0,0,0,8" LastChildFill="False">(.*?)</DockPanel>',{
 param($m)
 $body=$m.Groups[1].Value
 $body=$body.Replace('<StackPanel DockPanel.Dock="Right" Orientation="Horizontal"','<WrapPanel Orientation="Horizontal" Margin="0,6,0,0"').Replace('</StackPanel>','</WrapPanel>')
 return '<StackPanel DockPanel.Dock="Top" Margin="0,0,0,8">'+$body+'</StackPanel>'
})
[IO.File]::WriteAllText($path,$s)

