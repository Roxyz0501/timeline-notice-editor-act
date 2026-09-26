param([Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid stable version.' }
$zipFile = Get-Item -LiteralPath $ZipPath
if ($zipFile.Name -cne "TimelineNoticeEditor-v$Version.zip") { throw 'Unexpected asset name.' }
$manifest = [IO.Path]::ChangeExtension($zipFile.FullName, '.sha256')
$actualHash = (Get-FileHash -LiteralPath $zipFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Content -Raw -LiteralPath $manifest).Trim() -cne "$actualHash  $($zipFile.Name)") { throw 'Manifest mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipFile.FullName)
$stage = Join-Path $PSScriptRoot ('artifacts/verify-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    $expected = @('TimelineNoticeEditor.dll', 'TimelineNoticeEditor.Updater.exe', 'README.md')
    if ($archive.Entries.Count -ne 3) { throw 'Unexpected package count.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -cnotin $expected -or -not $seen.Add($entry.FullName) -or $entry.Length -gt 50MB) { throw 'Unexpected ZIP entry.' }
        $destination = Join-Path $stage $entry.FullName
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination)
        if ($entry.Name -ne 'README.md') {
            $assembly = [Reflection.AssemblyName]::GetAssemblyName($destination)
            if ($assembly.Name -cne [IO.Path]::GetFileNameWithoutExtension($entry.Name) -or $assembly.Version.ToString(3) -cne $Version) { throw 'Assembly identity/version mismatch.' }
            if ([Diagnostics.FileVersionInfo]::GetVersionInfo($destination).CompanyName -cne 'Roxyz0501') { throw 'Company mismatch.' }
        }
    }
} finally { $archive.Dispose() }
Write-Host "Verified $($zipFile.Name) and SHA-256."
