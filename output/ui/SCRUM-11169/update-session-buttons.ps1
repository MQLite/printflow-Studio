$path='C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/src/PrintFlow.App/Views/SessionScreenView.xaml'
$s=[IO.File]::ReadAllText($path)
$s=[regex]::Replace($s,'<Button(?=\s|>)','<local:SessionActionButton')
$s=$s.Replace('</Button>','</local:SessionActionButton>').Replace('<Button.Style>','<local:SessionActionButton.Style>').Replace('</Button.Style>','</local:SessionActionButton.Style>')
$s=$s.Replace('<Style TargetType="Button" BasedOn="{StaticResource RecommendedAction}" />','<Style TargetType="Button" BasedOn="{StaticResource RecommendedAction}" />'+[Environment]::NewLine+'        <Style TargetType="{x:Type local:SessionActionButton}" BasedOn="{StaticResource RecommendedAction}" />')
$s=$s.Replace('<TextBlock Text="{Binding HistoryStepName}"','<TextBlock x:Name="HistoryHeadingText" Focusable="True" Text="{Binding HistoryStepName}"')
$s=$s.Replace('<TextBlock Text="{Binding DraftLeaveHeading}"','<TextBlock x:Name="DraftLeaveHeadingText" Focusable="True" Text="{Binding DraftLeaveHeading}"')
[IO.File]::WriteAllText($path,$s)

