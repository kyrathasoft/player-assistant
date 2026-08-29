Option Explicit

Dim shell, fso, scriptPath, commandLine
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptPath = fso.GetAbsolutePathName(fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "keep-alive-mouse.ps1"))
If Not fso.FileExists(scriptPath) Then
    WScript.Quit 2
End If
commandLine = "PowerShell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & scriptPath & """ -DurationSeconds 55 -IntervalSeconds 30"
shell.Run commandLine, 0, True
Set fso = Nothing
Set shell = Nothing
