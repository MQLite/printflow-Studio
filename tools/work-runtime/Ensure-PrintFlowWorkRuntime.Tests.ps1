$script = Join-Path $PSScriptRoot 'Ensure-PrintFlowWorkRuntime.ps1'
Describe 'Work dependency startup' {
    BeforeAll { . $script }
    It 'reuses a ready service without launching another process' {
        Ensure-WorkService { $true } { throw 'Unexpected lookup' } { throw 'Duplicate launch' }
    }
    It 'starts a stopped service exactly once and waits for readiness' {
        $state = @{ started = 0; probes = 0 }
        Ensure-WorkService { $state.probes++; $state.probes -ge 3 } { $null } {
            $state.started++
            Get-Process -Id $PID
        }
        $state.started | Should Be 1
        $state.probes | Should Be 3
    }
    It 'waits for a running service that is still starting' {
        $state = @{ probes = 0 }
        Ensure-WorkService { $state.probes++; $state.probes -ge 2 } { Get-Process -Id $PID } { throw 'Duplicate launch' }
    }
    It 'reports a process that exits before it becomes ready' {
        { Ensure-WorkService { $false } { $null } { [pscustomobject]@{ HasExited = $true } } } | Should Throw 'Work service did not become ready.'
    }
}
