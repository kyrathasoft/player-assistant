# Sends a minimal interactive mouse input and requests that Windows keep the display awake.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$suppressionDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PlayerAssistant'
$suppressionPath = Join-Path $suppressionDirectory 'keep-alive-suppressed-until.txt'

if (Test-Path -LiteralPath $suppressionPath) {
    $rawUntil = (Get-Content -LiteralPath $suppressionPath -Raw).Trim()
    $until = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse($rawUntil, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$until)) {
        if ([DateTimeOffset]::UtcNow -lt $until.ToUniversalTime()) {
            exit 0
        }
    }
    Remove-Item -LiteralPath $suppressionPath -Force -ErrorAction SilentlyContinue
}

if (-not ('PlayerAssistant.KeepAlive.NativeMethods' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
namespace PlayerAssistant.KeepAlive {
    public static class NativeMethods {
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public MOUSEINPUT mi;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static bool KeepDisplayAwake() {
            return SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED) != 0;
        }

        public static bool MoveMouse(int dx, int dy) {
            var input = new INPUT {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE }
            };
            return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
        }
    }
}
'@
}

# Reset the display idle timer through the documented execution-state API.
[void][PlayerAssistant.KeepAlive.NativeMethods]::KeepDisplayAwake()

$direction = [Random]::new().Next(0, 4)
switch ($direction) {
    0 { [void][PlayerAssistant.KeepAlive.NativeMethods]::MoveMouse(1, 0) }
    1 { [void][PlayerAssistant.KeepAlive.NativeMethods]::MoveMouse(-1, 0) }
    2 { [void][PlayerAssistant.KeepAlive.NativeMethods]::MoveMouse(0, 1) }
    default { [void][PlayerAssistant.KeepAlive.NativeMethods]::MoveMouse(0, -1) }
}
