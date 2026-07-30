$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\word-count-publishing.ps1')

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)][object]$Expected,
        [Parameter(Mandatory = $true)][object]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (($Expected -join ',') -ne ($Actual -join ',')) {
        throw "$Message Expected '$($Expected -join ',')', got '$($Actual -join ',')'."
    }
}

function Invoke-TestTransaction {
    param(
        [string]$FailAt = '',
        [switch]$CleanupFails
    )

    $events = [Collections.Generic.List[string]]::new()
    $action = {
        param([string]$Name)
        $events.Add($Name)
        if ($FailAt -eq $Name) {
            throw "$Name failed"
        }
    }

    $parameters = @{
        StageSource = { & $action 'stage' }
        PublishSource = { & $action 'publish' }
        VerifySource = { & $action 'verify' }
        PublishBroker = {
            & $action 'broker'
            return @{ status = 'ok' }
        }
        RollbackSource = { & $action 'rollback' }
        CleanupSource = {
            $events.Add('cleanup')
            if ($CleanupFails) {
                throw 'cleanup failed'
            }
        }
    }

    $errorMessage = $null
    $result = $null
    try {
        $result = Invoke-WordCountPublishTransaction @parameters
    }
    catch {
        $errorMessage = $_.Exception.Message
    }

    return @{
        Events = $events.ToArray()
        Error = $errorMessage
        Result = $result
    }
}

$success = Invoke-TestTransaction
Assert-Equal @('stage', 'publish', 'verify', 'broker', 'cleanup') $success.Events 'Successful transaction order was incorrect.'
Assert-Equal 'ok' $success.Result.status 'Successful broker response was not returned.'

$stageFailure = Invoke-TestTransaction -FailAt 'stage'
Assert-Equal @('stage', 'cleanup') $stageFailure.Events 'Stage failure cleanup was incorrect.'
Assert-Equal 'stage failed' $stageFailure.Error 'Stage failure was not preserved.'

$verifyFailure = Invoke-TestTransaction -FailAt 'verify'
Assert-Equal @('stage', 'publish', 'verify', 'rollback') $verifyFailure.Events 'Verification failure rollback was incorrect.'
Assert-Equal 'verify failed' $verifyFailure.Error 'Verification failure was not preserved.'

$brokerFailure = Invoke-TestTransaction -FailAt 'broker'
Assert-Equal @('stage', 'publish', 'verify', 'broker', 'rollback') $brokerFailure.Events 'Broker failure rollback was incorrect.'
Assert-Equal 'broker failed' $brokerFailure.Error 'Broker failure was not preserved.'

$cleanupFailure = Invoke-TestTransaction -CleanupFails
Assert-Equal @('stage', 'publish', 'verify', 'broker', 'cleanup') $cleanupFailure.Events 'Cleanup failure altered transaction order.'
Assert-Equal 'ok' $cleanupFailure.Result.status 'Best-effort cleanup failure incorrectly failed the transaction.'

Write-Output 'Publisher transaction tests passed.'
