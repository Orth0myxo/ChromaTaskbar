param([switch]$Force)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$installRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$startMenuRoot = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\ChromaTaskbar'
$startupShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)) 'ChromaTaskbar.lnk'
$commandPrompt = Join-Path $env:WINDIR 'System32\cmd.exe'
$running = Get-Process -Name ChromaTaskbar -ErrorAction SilentlyContinue | Select-Object -First 1

if ($running) {
    [System.Windows.Forms.MessageBox]::Show(
        'Close ChromaTaskbar normally before uninstalling so it can restore TranslucentTB settings.',
        'Uninstall ChromaTaskbar', 'OK', 'Information') | Out-Null
    exit 1
}

if (-not $Force) {
    $answer = [System.Windows.Forms.MessageBox]::Show(
        "Remove ChromaTaskbar from this user account?`r`n`r`nOriginal TranslucentTB backups will be kept in Local AppData.",
        'Uninstall ChromaTaskbar', 'YesNo', 'Question')
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
        exit 0
    }
}

$shortcutWarning = $null
$startupWarning = $null
try {
    if (Test-Path -LiteralPath $startupShortcut) {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($startupShortcut)
        $isOurs = [string]::Equals([IO.Path]::GetFullPath($link.TargetPath), [IO.Path]::GetFullPath($commandPrompt), [StringComparison]::OrdinalIgnoreCase) -and
            $link.Arguments.IndexOf('Run ChromaTaskbar.cmd', [StringComparison]::OrdinalIgnoreCase) -ge 0
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link)
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
        if ($isOurs) {
            Remove-Item -LiteralPath $startupShortcut -Force
        }
    }
}
catch {
    $startupWarning = $_.Exception.Message
}

try {
    if (Test-Path -LiteralPath $startMenuRoot) {
        Remove-Item -LiteralPath $startMenuRoot -Recurse -Force
    }
}
catch {
    $shortcutWarning = $_.Exception.Message
}

try {
    Set-Location -LiteralPath $env:TEMP
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Uninstallation did not finish:`r`n$($_.Exception.Message)",
        'Uninstall ChromaTaskbar', 'OK', 'Error') | Out-Null
    exit 1
}

if ($shortcutWarning -or $startupWarning) {
    $warning = "ChromaTaskbar was removed, but some of its shortcuts could not be deleted. Start menu: $shortcutWarning Login startup: $startupWarning"
    if ($Force) {
        Write-Warning $warning
    }
    else {
        [System.Windows.Forms.MessageBox]::Show(
            $warning,
            'Uninstall ChromaTaskbar', 'OK', 'Warning') | Out-Null
    }
}
