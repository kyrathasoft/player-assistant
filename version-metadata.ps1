function Get-PlayerAssistantVersionMetadata {
    param([string]$RepoRoot = $PSScriptRoot)

    $metadataPath = Join-Path $RepoRoot 'version.props'
    if (!(Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
        throw "Version metadata is missing: $metadataPath"
    }

    [xml]$document = Get-Content -Raw -LiteralPath $metadataPath
    $properties = $document.Project.PropertyGroup
    $metadata = [pscustomobject]@{
        Version = [string]$properties.PlayerAssistantVersion
        AssemblyVersion = [string]$properties.PlayerAssistantAssemblyVersion
        PwaVersion = [string]$properties.PlayerAssistantPwaVersion
        PwaMetadataRevision = [int]$properties.PlayerAssistantPwaMetadataRevision
        PwaStylesRevision = [int]$properties.PlayerAssistantPwaStylesRevision
        PwaAppRevision = [int]$properties.PlayerAssistantPwaAppRevision
        PwaCacheRevision = [int]$properties.PlayerAssistantPwaCacheRevision
    }

    if ($metadata.Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
        throw "Desktop version '$($metadata.Version)' is invalid."
    }
    if ($metadata.PwaVersion -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
        throw "PWA version '$($metadata.PwaVersion)' is invalid."
    }
    $metadata | Add-Member -NotePropertyName InstallerVersion -NotePropertyValue (($metadata.Version -split '[-+]')[0])
    if ($metadata.AssemblyVersion -ne "$($metadata.InstallerVersion).0") {
        throw "Assembly version '$($metadata.AssemblyVersion)' must equal the numeric desktop version plus .0."
    }
    foreach ($propertyName in @('PwaMetadataRevision', 'PwaStylesRevision', 'PwaAppRevision', 'PwaCacheRevision')) {
        if ([int]$metadata.$propertyName -lt 1) {
            throw "Version metadata property '$propertyName' must be a positive integer."
        }
    }

    return $metadata
}
