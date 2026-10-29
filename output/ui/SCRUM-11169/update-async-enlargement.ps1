$path='C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/src/PrintFlow.App/ViewModels/SessionViewModel.cs'
$s=[IO.File]::ReadAllText($path)
$start=$s.IndexOf('    private async Task ContinueWithSizeAsync(')
$end=$s.IndexOf('    /// <summary>', $start)
$s=$s.Substring(0,$start)+@'
    private Task ContinueWithSizeAsync(CancellationToken cancellationToken)
    {
        if (_session is null || IsBusy || !IsLiveInteractionAllowed || !CanAuthoriseEnlargement)
            return Task.CompletedTask;
        Guid offer = _session.Sizing.EnlargementOfferId ?? Guid.Empty;
        return RunServiceAsync(id => _sessions.AuthoriseCurrentEnlargementAsync(
            id, offer, Environment.UserName, cancellationToken), cancellationToken);
    }

'@+$s.Substring($end)
[IO.File]::WriteAllText($path,$s)

