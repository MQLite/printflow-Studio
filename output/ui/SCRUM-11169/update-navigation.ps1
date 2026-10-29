$root = 'C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio'
$path = "$root/src/PrintFlow.App/ViewModels/SessionViewModel.Navigation.cs"
$s = [IO.File]::ReadAllText($path).Replace('NotifyFinalSave();','NotifyFinalSaveChanged();').Replace('s.Ordinal >= intent.Target.Ordinal','s.Ordinal >= intent.Target.Ordinal - 1')
[IO.File]::WriteAllText($path,$s)
$path = "$root/src/PrintFlow.App/ViewModels/SessionViewModel.cs"
$s = [IO.File]::ReadAllText($path)
$start=$s.IndexOf('    private async Task ReturnToSizeAndEditAsync(')
$end=$s.IndexOf('    /// <summary>Records exactly one operator-selected decimal target', $start)
$s=$s.Substring(0,$start)+@'
    private Task ReturnToSizeAndEditAsync(
        SizePreset? preset, TargetEdge? edge, decimal? millimetres, CancellationToken cancellationToken)
    {
        if (ReviewBoundsTarget is { } target)
            CaptureReturn(target, preset, edge, millimetres, editSize: true);
        return Task.CompletedTask;
    }

'@+$s.Substring($end)
[IO.File]::WriteAllText($path,$s)
$labels=@(
@('HistoryHeading','Step history','步骤历史'),
@('HistoryNotice','Read only. Historical results cannot be approved or used as the current result.','只读浏览。历史结果不能在此确认，也不会替换当前结果。'),
@('HistoryOpen','View history','查看历史'),
@('HistoryClose','Back to current step','回到当前步骤'),
@('HistoryValid','Valid record','记录有效'),
@('HistoryInvalid','Superseded record','已失效的记录'),
@('HistoryBytesReleased','Preview bytes released','预览文件已按保留规则释放'),
@('DraftLeaveHeading','Keep this unfinished input?','保留尚未提交的输入？'),
@('DraftLeaveNotice','This page has unfinished input. Keep editing, or discard this page draft and leave. Nothing has been confirmed or saved.','当前页面有尚未提交的输入。可继续编辑，或放弃本页输入后离开；这些输入尚未确认或保存。'),
@('KeepDraft','Keep editing','继续编辑'),
@('DiscardDraft','Discard input and leave','放弃输入并离开'),
@('ReturnImpact','Return to {0}. Steps to redo: {1}. Dependent dimensions, output results and approvals must be redone. Existing history is retained.','返回“{0}”。需要重新处理：{1}。依赖这些步骤的尺寸、输出结果及审核需要重新完成；已有历史记录保留。')
)
foreach($language in @('en','zh-CN')){
  $path=if($language -eq 'en'){"$root/src/PrintFlow.App/Resources/Strings.resx"}else{"$root/src/PrintFlow.App/Resources/Strings.zh-CN.resx"}
  $s=[IO.File]::ReadAllText($path)
  $nodes=foreach($label in $labels){$value=if($language -eq 'en'){$label[1]}else{$label[2]};'  <data name="Session_'+$label[0]+'" xml:space="preserve"><value>'+[Security.SecurityElement]::Escape($value)+'</value></data>'}
  $s=$s.Replace('</root>',($nodes -join [Environment]::NewLine)+[Environment]::NewLine+'</root>')
  [IO.File]::WriteAllText($path,$s)
}
$path="$root/src/PrintFlow.App/Resources/Strings.cs"
$s=[IO.File]::ReadAllText($path)
$members=foreach($label in $labels){'    internal static string Session_'+$label[0]+' => Get(nameof(Session_'+$label[0]+'));'}
$s=$s.Replace('internal static class Strings'+[Environment]::NewLine+'{','internal static class Strings'+[Environment]::NewLine+'{'+[Environment]::NewLine+($members -join [Environment]::NewLine))
[IO.File]::WriteAllText($path,$s)

