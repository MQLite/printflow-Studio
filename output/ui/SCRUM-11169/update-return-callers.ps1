$root='C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio'
foreach($file in @('FlexibleSizeUiTests.cs','FlexibleSizeRenderingTests.cs')){
 $path="$root/tests/PrintFlow.Tests/Integration/Ui/$file"
 $s=[IO.File]::ReadAllText($path)
 $s=[regex]::Replace($s,'(?m)^(\s*)await (\w+)\.(AdjustSize|ChangeSize)Command.ExecuteAsync\(null\);', '$0'+[Environment]::NewLine+'$1$2.IsConfirmingReturn.ShouldBeTrue();'+[Environment]::NewLine+'$1await $2.ConfirmReturnCommand.ExecuteAsync(null);')
 [IO.File]::WriteAllText($path,$s)
}

