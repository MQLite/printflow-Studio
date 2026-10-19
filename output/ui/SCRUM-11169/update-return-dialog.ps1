$root='C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio'
$path="$root/src/PrintFlow.App/ViewModels/SessionViewModel.Correction.cs"
$s=[IO.File]::ReadAllText($path).Replace('=> IsLiveInteractionAllowed &&','=> CanInteractWithLivePage &&')
[IO.File]::WriteAllText($path,$s)
$path="$root/src/PrintFlow.App/Views/SessionScreenView.xaml"
$s=[IO.File]::ReadAllText($path).Replace('IsEnabled="{Binding IsLiveInteractionAllowed}"','IsEnabled="{Binding CanInteractWithLivePage}"')
$index=$s.IndexOf('Visibility="{Binding IsConfirmingReturn, Converter=')
$start=$s.LastIndexOf('<Border',$index)
$end=$s.IndexOf('</Border>',$index)+'</Border>'.Length
$s=$s.Remove($start,$end-$start)
$overlay=@'
        <Border Grid.Row="4" Grid.RowSpan="2" Grid.ColumnSpan="2" Padding="24" Margin="20" MaxWidth="650"
                VerticalAlignment="Center" Background="{DynamicResource PF.Brush.Surface}"
                BorderBrush="{DynamicResource PF.Brush.Primary}" BorderThickness="2" Panel.ZIndex="9"
                Visibility="{Binding IsConfirmingReturn, Converter={StaticResource BoolToVisibility}}"
                AutomationProperties.AutomationId="Session.ReturnConfirmation">
            <StackPanel>
                <TextBlock x:Name="ReturnHeadingText" Focusable="True" Text="{Binding ReturnHeading}" FontSize="20" FontWeight="SemiBold" />
                <TextBlock Text="{Binding ReturnImpact}" TextWrapping="Wrap" Margin="0,12,0,18" />
                <WrapPanel>
                    <local:SessionActionButton Content="{Binding ConfirmReturnLabel}" Command="{Binding ConfirmReturnCommand}"
                            AutomationProperties.AutomationId="Session.ConfirmReturn" Margin="0,0,12,8" />
                    <local:SessionActionButton Content="{Binding CancelReturnLabel}" Command="{Binding CancelReturnCommand}"
                            AutomationProperties.AutomationId="Session.CancelReturn" Margin="0,0,0,8" />
                </WrapPanel>
            </StackPanel>
        </Border>
'@
$end=$s.LastIndexOf('    </Grid>')
$s=$s.Insert($end,$overlay+[Environment]::NewLine)
[IO.File]::WriteAllText($path,$s)

