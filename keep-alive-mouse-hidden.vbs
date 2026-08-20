Option Explicit

Dim shell, scriptPath, commandLine
Set shell = CreateObject("WScript.Shell")
scriptPath = "C:\repos\player-assistant\keep-alive-mouse.ps1"
commandLine = "PowerShell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & scriptPath & """"
shell.Run commandLine, 0, True
Set shell = Nothing
