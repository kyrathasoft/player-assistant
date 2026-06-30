param(
    [int]$ProcessId,
    [string]$FromDir,
    [string]$ToDir
)

try {
    Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
}
finally {
    if (Test-Path -LiteralPath $FromDir) {
        New-Item -ItemType Directory -Force -Path $ToDir | Out-Null
        Get-ChildItem -LiteralPath $FromDir -File -ErrorAction SilentlyContinue | ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination (Join-Path $ToDir $_.Name) -Force
        }
        Remove-Item -LiteralPath $FromDir -Force -ErrorAction SilentlyContinue
    }
}
