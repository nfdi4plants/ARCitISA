; ARCitISA additions to the electron-builder NSIS installer. electron-builder inserts these macros at
; fixed points of its templates (node_modules/app-builder-lib/templates/nsis). makensis runs with -WX,
; so a variable that the installer or the uninstaller declares but never uses fails the build.

!include LogicLib.nsh
!include nsDialogs.nsh

!ifndef BUILD_UNINSTALLER
  Var oldSwateDir
  Var oldArcitisaDir
  Var oldSquirrelExitCode
  Var createDesktopShortcut
  Var desktopShortcutCheckbox
!endif

!macro quitBecauseOldVersionRemains MESSAGE
  MessageBox MB_OK|MB_ICONSTOP "${MESSAGE}" /SD IDOK
  SetErrorLevel 2
  Quit
!macroend

; Squirrel leaves Update.exe and a .dead file behind after an uninstall. Only an app-<version> folder
; means that the old version is still installed.
!macro removeOldSquirrelInstall DIR
  ${If} ${FileExists} "${DIR}\app-*"
    ClearErrors
    ExecWait '"${DIR}\Update.exe" --uninstall' $oldSquirrelExitCode
    ${If} ${Errors}
      !insertmacro quitBecauseOldVersionRemains "Setup could not start ${DIR}\Update.exe to remove the older version of ARCitISA.$\r$\n$\r$\nDelete the folder ${DIR}, then run setup again."
    ${EndIf}
    ${If} $oldSquirrelExitCode != 0
      !insertmacro quitBecauseOldVersionRemains "The older version of ARCitISA could not be removed. Its uninstaller exited with code $oldSquirrelExitCode.$\r$\n$\r$\nClose all ARCitISA windows, then run setup again."
    ${EndIf}
    ${If} ${FileExists} "${DIR}\app-*"
      !insertmacro quitBecauseOldVersionRemains "The older version of ARCitISA was not removed completely. Its files are still in ${DIR}.$\r$\n$\r$\nClose all ARCitISA windows, then run setup again."
    ${EndIf}
  ${EndIf}
!macroend

; ARCitISA 3.x was installed by Squirrel, up to 3.4.0-rc.2 into %LocalAppData%\swate and from 3.4.0
; into %LocalAppData%\arcitisa. Without this check both end up installed next to this version.
; Remove in 5.0.0 (docs/maintainer/ObsoleteRemoveOnNextMajor.md).
!macro customInit
  StrCpy $createDesktopShortcut ${BST_UNCHECKED}

  ; An "all users" install starts setup again with admin rights. The first instance has already done
  ; the check, and the elevated one may run as another user.
  ${IfNot} ${UAC_IsInnerInstance}
    ; Not $LOCALAPPDATA: it points to ProgramData once a previous install sets the mode to all users.
    ExpandEnvStrings $oldSwateDir "%LOCALAPPDATA%\swate"
    ExpandEnvStrings $oldArcitisaDir "%LOCALAPPDATA%\arcitisa"

    ${If} ${FileExists} "$oldSwateDir\app-*"
    ${OrIf} ${FileExists} "$oldArcitisaDir\app-*"
      ${If} ${Cmd} `MessageBox MB_YESNO|MB_ICONQUESTION "An older version of ARCitISA was found. It must be removed before this version can be installed. Your settings and signed-in accounts are kept.$\r$\n$\r$\nRemove it now?" /SD IDNO IDNO`
        SetErrorLevel 1
        Quit
      ${EndIf}

      !insertmacro removeOldSquirrelInstall $oldSwateDir
      !insertmacro removeOldSquirrelInstall $oldArcitisaDir
    ${EndIf}
  ${EndIf}
!macroend

; Shown after the folder page. A silent install skips it and creates no desktop shortcut.
!macro customPageAfterChangeDir
  Page custom desktopShortcutPageCreate desktopShortcutPageLeave

  Function desktopShortcutPageCreate
    ${If} ${isUpdated}
      Abort
    ${EndIf}

    !insertmacro MUI_HEADER_TEXT "Shortcuts" "Choose the shortcuts that setup creates."

    nsDialogs::Create 1018
    Pop $0
    ${If} $0 == error
      Abort
    ${EndIf}

    ${NSD_CreateCheckbox} 0 0 100% 12u "Create a desktop shortcut"
    Pop $desktopShortcutCheckbox
    ${NSD_SetState} $desktopShortcutCheckbox $createDesktopShortcut

    nsDialogs::Show
  FunctionEnd

  Function desktopShortcutPageLeave
    ${NSD_GetState} $desktopShortcutCheckbox $createDesktopShortcut
  FunctionEnd
!macroend

; An update keeps the shortcut the user already has. The old uninstaller gets --updated as well, so it
; leaves the shortcut in place (see customUnInstall).
!macro customInstall
  ${IfNot} ${isUpdated}
  ${AndIf} $createDesktopShortcut == ${BST_CHECKED}
    CreateShortCut "$newDesktopLink" "$appExe" "" "$appExe" 0 "" "" "${APP_DESCRIPTION}"
    ClearErrors
    ; Same as electron-builder's own shortcuts, so the window groups with the shortcut in the taskbar.
    WinShell::SetLnkAUMI "$newDesktopLink" "${APP_ID}"
    System::Call 'Shell32::SHChangeNotify(i 0x8000000, i 0, i 0, i 0)'
  ${EndIf}
!macroend

; Setup runs the uninstaller of the installed version with --updated before it installs a new one.
!macro customUnInstall
  ${IfNot} ${isUpdated}
    WinShell::UninstShortcut "$newDesktopLink"
    Delete "$newDesktopLink"
  ${EndIf}
!macroend
