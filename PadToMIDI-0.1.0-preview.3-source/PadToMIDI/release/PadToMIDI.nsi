Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"

Name "PadToMIDI Preview"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\PadToMIDI"
InstallDirRegKey HKCU "Software\PadToMIDI\Installer" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID zlib
BrandingText "PadToMIDI ${VERSION}"
VIProductVersion "${FILEVERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "PadToMIDI Preview"
VIAddVersionKey /LANG=1033 "FileDescription" "PadToMIDI per-user installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "PadToMIDI contributors"
!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Install PadToMIDI ${VERSION} for your user account.$\r$\n$\r$\nWindows 11 25H2 or newer (x64) is required. This app uses preview Windows MIDI Services APIs. Windows MIDI Services and loopback/synth transports are separate prerequisites.$\r$\n$\r$\nSaved profiles are kept when upgrading or uninstalling."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\PadToMIDI.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var TestMode

!macro RequireClosed
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\PadToMIDI.Desktop") p.r0'
  ${If} $0 == 0
    ; Also protect an installation when a version from before the rename is running.
    System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\GamepadMidi.Desktop") p.r0'
  ${EndIf}
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    IfSilent +2
      MessageBox MB_OK|MB_ICONEXCLAMATION "Close PadToMIDI before installing or uninstalling."
    SetErrorLevel 5
    Quit
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext current
  SetRegView 64
  ${IfNot} ${RunningX64}
    IfSilent +2
      MessageBox MB_OK|MB_ICONSTOP "This package requires 64-bit Windows 11."
    SetErrorLevel 2
    Quit
  ${EndIf}
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ReadRegStr $1 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "InstallationType"
  ${If} $0 < ${MINBUILD}
  ${OrIf} $1 != "Client"
    IfSilent +2
      MessageBox MB_OK|MB_ICONSTOP "Windows 11 25H2 or newer is required by the preview MIDI API."
    SetErrorLevel 2
    Quit
  ${EndIf}
  !insertmacro RequireClosed
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/TESTMODE" $1
  ${IfNot} ${Errors}
    StrCpy $TestMode "1"
  ${EndIf}
FunctionEnd

Section "PadToMIDI" Main
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File /r "${PAYLOAD}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ${If} $TestMode == "1"
    FileOpen $0 "$INSTDIR\installer-test-mode" w
    FileWrite $0 "Test installation; no registry or shortcut integration."
    FileClose $0
  ${Else}
    CreateDirectory "$SMPROGRAMS\PadToMIDI"
    CreateShortcut "$SMPROGRAMS\PadToMIDI\PadToMIDI.lnk" "$INSTDIR\PadToMIDI.exe"
    CreateShortcut "$SMPROGRAMS\PadToMIDI\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\PadToMIDI\Installer" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "DisplayName" "PadToMIDI Preview"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "DisplayVersion" "${VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "DisplayIcon" "$INSTDIR\PadToMIDI.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI" "NoRepair" 1
  ${EndIf}
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro RequireClosed
FunctionEnd

Section "Uninstall"
  IfFileExists "$INSTDIR\installer-test-mode" testmode normal
normal:
  ReadRegStr $0 HKCU "Software\PadToMIDI\Installer" "InstallLocation"
  StrCmp $0 $INSTDIR 0 testmode
  Delete "$SMPROGRAMS\PadToMIDI\PadToMIDI.lnk"
  Delete "$SMPROGRAMS\PadToMIDI\Uninstall.lnk"
  RMDir "$SMPROGRAMS\PadToMIDI"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PadToMIDI"
  DeleteRegKey HKCU "Software\PadToMIDI\Installer"
testmode:
  ; Generated explicit file list: no recursive deletion and no profile-library deletion.
  !include "${UNINSTALLFILES}"
  Delete "$INSTDIR\installer-test-mode"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
