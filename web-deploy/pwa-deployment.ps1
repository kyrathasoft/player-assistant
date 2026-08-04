Set-StrictMode -Version Latest

function Invoke-PwaDeploymentTransaction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][scriptblock]$InstallRelease,
        [Parameter(Mandatory = $true)][scriptblock]$VerifyPublic,
        [Parameter(Mandatory = $true)][scriptblock]$CommitRelease,
        [Parameter(Mandatory = $true)][scriptblock]$RollbackRelease
    )

    $installed = $false
    try {
        & $InstallRelease
        $installed = $true
        & $VerifyPublic
        & $CommitRelease
    }
    catch {
        $deploymentError = $_
        if ($installed) {
            try {
                & $RollbackRelease
            }
            catch {
                throw [InvalidOperationException]::new(
                    "PWA deployment failed: $($deploymentError.Exception.Message) Rollback failed: $($_.Exception.Message)",
                    $deploymentError.Exception)
            }
        }
        throw $deploymentError
    }
}
