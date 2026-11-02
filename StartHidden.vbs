Option Explicit

' Скрытый запуск Toolkit при входе в систему.
' Пересобирает exe, если любой .cs новее собранного бинарника.

Dim fso, sh, base, ps, exePath, buildPs, srcDir

Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")

base = fso.GetParentFolderName(WScript.ScriptFullName)
ps = sh.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
exePath = base & "\Toolkit.exe"
buildPs = base & "\build.ps1"
srcDir = base & "\src"

If NeedRebuild(fso, exePath, srcDir) Then
  sh.Run """" & ps & """ -NoProfile -ExecutionPolicy Bypass -File """ & buildPs & """", 0, True
End If

If fso.FileExists(exePath) Then
  sh.Run """" & exePath & """ run", 0, False
End If

Function NeedRebuild(fileSystem, exe, sourceDir)
  NeedRebuild = False

  If Not fileSystem.FileExists(exe) Then
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
