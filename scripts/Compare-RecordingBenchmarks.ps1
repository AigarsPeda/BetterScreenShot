param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Current
)

$before = Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json
$after = Get-Content -LiteralPath $Current -Raw | ConvertFrom-Json

function Get-Median($samples, [string]$metric) {
    $values = @($samples | ForEach-Object { [double]($_.$metric) } | Sort-Object)
    if ($values.Count -eq 0) { throw "No samples for $metric." }
    $middle = [int][math]::Floor($values.Count / 2)
    if ($values.Count % 2) { return $values[$middle] }
    return ($values[$middle - 1] + $values[$middle]) / 2
}

$rows = foreach ($workload in $before.Workloads) {
    $matching = @($after.Workloads | Where-Object Name -eq $workload.Name)
    if ($matching.Count -ne 1 -or $matching[0].Width -ne $workload.Width -or
        $matching[0].Height -ne $workload.Height -or $matching[0].Frames -ne $workload.Frames -or
        $matching[0].Format -ne $workload.Format -or $matching[0].Quality -ne $workload.Quality) {
        throw "Workload '$($workload.Name)' does not match between reports."
    }
    $oldSamples = @($before.Samples | Where-Object Workload -eq $workload.Name)
    $newSamples = @($after.Samples | Where-Object Workload -eq $workload.Name)
    foreach ($metric in @('WallMs', 'CpuMs', 'AllocatedBytes', 'FileBytes')) {
        $oldValue = Get-Median $oldSamples $metric
        $newValue = Get-Median $newSamples $metric
        [pscustomobject]@{
            Workload = $workload.Name
            Metric = $metric
            Baseline = [math]::Round($oldValue, 1)
            Current = [math]::Round($newValue, 1)
            ChangePercent = [math]::Round(100 * ($newValue - $oldValue) / $oldValue, 1)
        }
    }
}
$rows | Format-Table -AutoSize
