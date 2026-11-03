Option Explicit

' Скрытый запуск Toolkit при входе в систему.
'
' Папка Toolkit (где лежит этот скрипт) может быть общей и только для чтения, поэтому exe собираются
' локально в %TEMP%\Toolkit встроенным csc.exe и запускаются оттуда. Данные (config.json, profiles,
' reg, data, logs) остаются в папке Toolkit: путь к ней передаётся в exe ключом --home.
' Пересборка только если exe нет или любой .cs новее собранного exe.

Dim fso, sh, base, ps, outDir, exePath, guardPath, buildPs, srcDir

Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")

base = fso.GetParentFolderName(WScript.ScriptFullName)
ps = sh.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
outDir = sh.ExpandEnvironmentStrings("%TEMP%") & "\Toolkit"
exePath = outDir & "\Toolkit.exe"
guardPath = outDir & "\tkguard.exe"
buildPs = base & "\build.ps1"
srcDir = base & "\src"

If NeedRebuild(fso, exePath, guardPath, srcDir) Then
  sh.Run """" & ps & """ -NoProfile -ExecutionPolicy Bypass -File """ & buildPs & """ -Out """ & outDir & """", 0, True
End If

If fso.FileExists(exePath) Then
  sh.Run """" & exePath & """ run --home """ & base & """", 0, False
End If

Function NeedRebuild(fileSystem, exe, guardExe, sourceDir)
  NeedRebuild = False

  If Not fileSystem.FileExists(exe) Or Not fileSystem.FileExists(guardExe) Then
    NeedRebuild = True
    Exit Function
  End If

  If Not fileSystem.FolderExists(sourceDir) Then
    Exit Function
  End If

  NeedRebuild = FolderHasNewerSource(fileSystem, sourceDir, fileSystem.GetFile(exe).DateLastModified)
End Function

Function FolderHasNewerSource(fileSystem, folderPath, exeDate)
  Dim folder, file, subFolder
  FolderHasNewerSource = False
  Set folder = fileSystem.GetFolder(folderPath)

  For Each file In folder.Files
    If LCase(fileSystem.GetExtensionName(file.Name)) = "cs" Then
      If CDate(file.DateLastModified) > CDate(exeDate) Then
        FolderHasNewerSource = True
        Exit Function
      End If
    End If
  Next

  For Each subFolder In folder.SubFolders
    If FolderHasNewerSource(fileSystem, subFolder.Path, exeDate) Then
      FolderHasNewerSource = True
      Exit Function
    End If
  Next
End Function
