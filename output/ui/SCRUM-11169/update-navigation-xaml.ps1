$root='C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio'
$path="$root/src/PrintFlow.App/Views/SessionScreenView.xaml"
$s=[IO.File]::ReadAllText($path)
$end=$s.LastIndexOf('    </Grid>')
$overlay=@'
        <Border Grid.Row="4" Grid.Column="1" Margin="0,14,0,0" Padding="20" Background="{DynamicResource PF.Brush.Surface}"
                AutomationProperties.AutomationId="Session.HistoryWorkspace"
                Visibility="{Binding IsViewingHistory, Converter={StaticResource BoolToVisibility}}">
            <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                <StackPanel>
                    <TextBlock Text="{Binding HistoryStepName}" FontSize="22" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding HistoryNotice}" TextWrapping="Wrap" Margin="0,12,0,16" />
                    <ItemsControl ItemsSource="{Binding HistoryFacts}">
                        <ItemsControl.ItemTemplate><DataTemplate>
                            <Border Padding="12" Margin="0,0,0,10" BorderBrush="{DynamicResource PF.Brush.Border}" BorderThickness="1">
                                <TextBlock Text="{Binding}" TextWrapping="Wrap" />
                            </Border>
                        </DataTemplate></ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button Content="{Binding HistoryCloseLabel}" Command="{Binding CloseHistoryCommand}"
                            HorizontalAlignment="Left" Margin="0,16,0,0" AutomationProperties.AutomationId="Session.CloseHistory" />
                </StackPanel>
            </ScrollViewer>
        </Border>
        <Border Grid.Row="4" Grid.ColumnSpan="2" Grid.RowSpan="2" Background="{DynamicResource PF.Brush.Surface}"
                BorderBrush="{DynamicResource PF.Brush.Primary}" BorderThickness="2" Padding="24" Margin="20"
                MaxWidth="600" VerticalAlignment="Center" Panel.ZIndex="10"
                AutomationProperties.AutomationId="Session.DraftLeave"
                Visibility="{Binding IsConfirmingDraftLeave, Converter={StaticResource BoolToVisibility}}">
            <StackPanel>
                <TextBlock Text="{Binding DraftLeaveHeading}" FontSize="20" FontWeight="SemiBold" />
                <TextBlock Text="{Binding DraftLeaveNotice}" TextWrapping="Wrap" Margin="0,12,0,18" />
                <WrapPanel>
                    <Button Content="{Binding KeepDraftLabel}" Command="{Binding KeepDraftCommand}" Margin="0,0,12,8"
                            AutomationProperties.AutomationId="Session.KeepDraft" />
                    <Button Content="{Binding DiscardDraftLabel}" Command="{Binding DiscardDraftAndLeaveCommand}" Margin="0,0,0,8"
                            AutomationProperties.AutomationId="Session.DiscardDraft" />
                </WrapPanel>
            </StackPanel>
        </Border>
'@
$s=$s.Substring(0,$end)+$overlay+[Environment]::NewLine+$s.Substring($end)
$start=$s.IndexOf('                    <Border', $s.IndexOf('Return to an earlier step (Epic'))
$end=$s.IndexOf('                    </Border>', $s.IndexOf('Command="{Binding CancelReturnCommand}"',$start))+'                    </Border>'.Length
$block=$s.Substring($start,$end-$start)
$block='                    <Expander Header="{Binding ReturnHeading}" IsExpanded="{Binding IsConfirmingReturn, Mode=OneWay}" Visibility="{Binding CanReturnToStep, Converter={StaticResource BoolToVisibility}}">'+[Environment]::NewLine+$block+[Environment]::NewLine+'                    </Expander>'
$s=$s.Substring(0,$start)+$block+$s.Substring($end)
[IO.File]::WriteAllText($path,$s)

