[CmdletBinding()]
param(
    [ValidateRange(5, 300)][int]$IntervalSeconds = 30,
    [ValidateRange(0, 3600)][int]$DurationSeconds = 0,
    [string]$StatusPath = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PlayerAssistant\keep-alive-status.json')
)
$ErrorActionPreference = 'Stop'
$suppressionDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PlayerAssistant'
$suppressionPath = Join-Path $suppressionDirectory 'keep-alive-suppressed-until.txt'
if (Test-Path -LiteralPath $suppressionPath) {
    $rawUntil = (Get-Content -LiteralPath $suppressionPath -Raw).Trim()
    $until = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse($rawUntil, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$until)) {
        if ([DateTimeOffset]::UtcNow -lt $until.ToUniversalTime()) { exit 0 }
    }
    Remove-Item -LiteralPath $suppressionPath -Force -ErrorAction SilentlyContinue
}
$MaxDiagnosticLength = 1024
function Write-Status {
    param([string]$State,[string]$Operation,[int]$ErrorCode = 0,[string]$Message = '')
    try {
        $diagnostic = $Message
        if ($diagnostic.Length -gt $MaxDiagnosticLength) { $diagnostic = $diagnostic.Substring(0, $MaxDiagnosticLength) }
        $directory = Split-Path -Parent ([IO.Path]::GetFullPath($StatusPath))
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $payload = [ordered]@{ schema_version = 1; state = $State; operation = $Operation; error_code = $ErrorCode; diagnostic = $diagnostic; recorded_at_utc = [DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json -Compress
        if ($payload.Length -gt $MaxDiagnosticLength) { $payload = $payload.Substring(0, $MaxDiagnosticLength) }
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($StatusPath), $payload, [Text.UTF8Encoding]::new($false))
    } catch { }
}
function Invoke-KeepAlive {
    try {
        $display = [PlayerAssistant.KeepAlive.NativeMethods]::KeepDisplayAwake()
        if (-not $display.Success) { throw "SetThreadExecutionState failed with Win32 error $($display.ErrorCode)." }
        $direction = [Random]::new().Next(0, 4)
        $dx = 0; $dy = 0
        switch ($direction) { 0 {$dx=1} 1 {$dx=-1} 2 {$dy=1} default {$dy=-1} }
        $input = [PlayerAssistant.KeepAlive.NativeMethods]::MoveMouse($dx, $dy)
        if (-not $input.Success) { throw "SendInput failed with Win32 error $($input.ErrorCode)." }
        Write-Status 'success' 'display-and-input'
    } catch {
        $code = 0
        if ($_.Exception.Message -match 'error (\d+)') { $code = [int]$Matches[1] }
        Write-Status 'failure' 'display-and-input' $code $_.Exception.Message
        throw
    }
}
$resolvedStatusPath = [IO.Path]::GetFullPath($StatusPath)
if (-not [IO.Path]::IsPathRooted($resolvedStatusPath)) { throw 'StatusPath must be absolute.' }
$StatusPath = $resolvedStatusPath
if (-not ('PlayerAssistant.KeepAlive.NativeMethods' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
namespace PlayerAssistant.KeepAlive {
    public sealed class NativeResult { public readonly bool Success; public readonly int ErrorCode; public NativeResult(bool success, int errorCode) { Success = success; ErrorCode = errorCode; } }
    public static class NativeMethods {
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }
        [DllImport("kernel32.dll", SetLastError=true)] private static extern uint SetThreadExecutionState(uint flags);
        [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        public static NativeResult KeepDisplayAwake() { var result = SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED); return new NativeResult(result != 0, result == 0 ? Marshal.GetLastWin32Error() : 0); }
        public static NativeResult MoveMouse(int dx, int dy) { var input = new INPUT { type=INPUT_MOUSE, mi=new MOUSEINPUT { dx=dx, dy=dy, dwFlags=MOUSEEVENTF_MOVE } }; var result=SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT))); return new NativeResult(result==1,result==1?0:Marshal.GetLastWin32Error()); }
    }
}
'@
}
$start = [DateTimeOffset]::UtcNow
Invoke-KeepAlive
while ($DurationSeconds -gt 0 -and ([DateTimeOffset]::UtcNow - $start).TotalSeconds -lt $DurationSeconds) {
    $remaining = $DurationSeconds - [int](([DateTimeOffset]::UtcNow - $start).TotalSeconds)
    Start-Sleep -Seconds ([Math]::Min($IntervalSeconds, [Math]::Max(1, $remaining)))
    if (([DateTimeOffset]::UtcNow - $start).TotalSeconds -lt $DurationSeconds) { Invoke-KeepAlive }
}
