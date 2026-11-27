; ARCitISA additions to the electron-builder NSIS installer. electron-builder inserts these macros at
; fixed points of its templates (node_modules/app-builder-lib/templates/nsis). makensis runs with -WX,
; so a variable that the installer or the uninstaller declares but never uses fails the build.

!include LogicLib.nsh
!include nsDialogs.nsh

!ifndef BUILD_UNINSTALLER
  Var oldSwateDir
  Var oldArcitisaDir
  Var oldSquirrelExitCode
  Var oldSquirrelFindHandle
  Var oldSquirrelAppDir
  Var createDesktopShortcut
  Var desktopShortcutCheckbox
  Var desktopShortcutPageShown
!else
  Var userDataDir
  Var legacyUserDataDir
  Var userDataFolders
!endif

!macro quitBecauseOldVersionRemains MESSAGE
  MessageBox MB_OK|MB_ICONSTOP "${MESSAGE}" /SD IDOK
  SetErrorLevel 2
  Quit
!macroend

; Squirrel's uninstaller stops the old app, removes its entry in Settings > Apps and its shortcuts,
; deletes its files and writes a .dead file. Update.exe stays, it cannot delete itself. It deletes
; right after stopping the app, while the stopped processes may still hold their files, and leaves
; what it could not delete. Run again, it fails (exit code -1). So setup runs it only when there is no
; .dead file yet, and deletes the remaining app-<version> folders itself.
!macro removeOldSquirrelInstall DIR
  ${If} ${FileExists} "${DIR}\app-*"
    ${IfNot} ${FileExists} "${DIR}\.dead"
      ClearErrors
      ExecWait '"${DIR}\Update.exe" --uninstall' $oldSquirrelExitCode
      ${If} ${Errors}
        !insertmacro quitBecauseOldVersionRemains "Setup could not start ${DIR}\Update.exe to remove the older version of ARCitISA.$\r$\n$\r$\nDelete the folder ${DIR}, then run setup again."
      ${EndIf}
      ${If} $oldSquirrelExitCode != 0
        !insertmacro quitBecauseOldVersionRemains "The older version of ARCitISA could not be removed. Its uninstaller exited with code $oldSquirrelExitCode.$\r$\n$\r$\nClose all ARCitISA windows, then run setup again."
      ${EndIf}
    ${EndIf}

    ${Do}
      FindFirst $oldSquirrelFindHandle $oldSquirrelAppDir "${DIR}\app-*"
      FindClose $oldSquirrelFindHandle
      ${If} $oldSquirrelAppDir == ""
        ${ExitDo}
      ${EndIf}
      RMDir /r "${DIR}\$oldSquirrelAppDir"
      ${If} ${FileExists} "${DIR}\$oldSquirrelAppDir"
        !insertmacro quitBecauseOldVersionRemains "The older version of ARCitISA was not removed completely. Its files are still in ${DIR}\$oldSquirrelAppDir.$\r$\n$\r$\nClose all ARCitISA windows, then run setup again."
      ${EndIf}
    ${Loop}
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

; Shown after the folder page, so that the page shows what will be on the desktop. The first time it
; shows, the box is ticked when the installed version has a desktop shortcut: on the desktop of the
; chosen install mode, or for "all users" also on the current user's own desktop. A silent install
; skips the page (see customInstall).
!macro customPageAfterChangeDir
  Page custom desktopShortcutPageCreate desktopShortcutPageLeave

  Function desktopShortcutPageCreate
    ${If} ${isUpdated}
      Abort
    ${EndIf}

    ${If} $desktopShortcutPageShown != "1"
      StrCpy $desktopShortcutPageShown "1"
      ${If} ${FileExists} "$DESKTOP\${SHORTCUT_NAME}.lnk"
        StrCpy $createDesktopShortcut ${BST_CHECKED}
      ${ElseIf} $installMode == "all"
        SetShellVarContext current
        ${If} ${FileExists} "$DESKTOP\${SHORTCUT_NAME}.lnk"
          StrCpy $createDesktopShortcut ${BST_CHECKED}
        ${EndIf}
        SetShellVarContext all
      ${EndIf}
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

!macro createDesktopLink
  CreateShortCut "$newDesktopLink" "$appExe" "" "$appExe" 0 "" "" "${APP_DESCRIPTION}"
  ClearErrors
  ; Same as electron-builder's own shortcuts, so the window groups with the shortcut in the taskbar.
  WinShell::SetLnkAUMI "$newDesktopLink" "${APP_ID}"
!macroend

; Setup runs the uninstaller of the installed version with --updated first, which keeps the desktop
; shortcut (see customUnInstall). Here the shortcut follows the box: ticked creates it or points it
; at the new folder, unticked removes it. Installing for all users also uninstalls a per-user
; install of the current user; the shortcut on the public desktop replaces its desktop shortcut.
; A silent install keeps an existing shortcut and points it at the new folder, and creates none.
; An updater run (--updated) leaves everything as it is.
!macro customInstall
  ${IfNot} ${isUpdated}
    ${If} $desktopShortcutPageShown == "1"
      ${If} $createDesktopShortcut == ${BST_CHECKED}
        !insertmacro createDesktopLink
      ${ElseIf} ${FileExists} "$newDesktopLink"
        WinShell::UninstShortcut "$newDesktopLink"
        Delete "$newDesktopLink"
      ${EndIf}
      ${If} $installMode == "all"
        SetShellVarContext current
        ${If} ${FileExists} "$DESKTOP\${SHORTCUT_NAME}.lnk"
          WinShell::UninstShortcut "$DESKTOP\${SHORTCUT_NAME}.lnk"
          Delete "$DESKTOP\${SHORTCUT_NAME}.lnk"
        ${EndIf}
        SetShellVarContext all
      ${EndIf}
    ${ElseIf} ${FileExists} "$newDesktopLink"
      !insertmacro createDesktopLink
    ${EndIf}
    System::Call 'Shell32::SHChangeNotify(i 0x8000000, i 0, i 0, i 0)'
  ${EndIf}
!macroend

; Asks whether to delete the user data: %AppData%\ARCitISA, and %AppData%\Swate, where 3.x kept it.
; A silent uninstall cannot ask and keeps it: deployment tools (winget, Intune) may uninstall silently
; as the first step of an update. Not $APPDATA: it points to ProgramData in an all-users uninstall.
!macro askToDeleteUserData
  ExpandEnvStrings $userDataDir "%APPDATA%\ARCitISA"
  ExpandEnvStrings $legacyUserDataDir "%APPDATA%\Swate"
  StrCpy $userDataFolders ""
  ${If} ${FileExists} "$userDataDir\*.*"
    StrCpy $userDataFolders "$userDataFolders$userDataDir$\r$\n"
  ${EndIf}
  ${If} ${FileExists} "$legacyUserDataDir\*.*"
    StrCpy $userDataFolders "$userDataFolders$legacyUserDataDir$\r$\n"
  ${EndIf}

  ${If} $userDataFolders != ""
    ${If} ${Cmd} `MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Do you also want to delete your ARCitISA settings, signed-in accounts and list of recent ARCs?$\r$\n$\r$\nThey are stored in:$\r$\n$userDataFolders$\r$\nYes deletes them. If you install ARCitISA again, it starts with default settings and you sign in again.$\r$\nNo keeps them for a later install.$\r$\n$\r$\nYour ARCs are not deleted. They stay in their folders." /SD IDNO IDYES`
      RMDir /r "$userDataDir"
      RMDir /r "$legacyUserDataDir"
      ${If} ${FileExists} "$userDataDir\*.*"
      ${OrIf} ${FileExists} "$legacyUserDataDir\*.*"
        MessageBox MB_OK|MB_ICONEXCLAMATION "Some of your ARCitISA data could not be deleted. Delete these folders yourself:$\r$\n$\r$\n$userDataFolders" /SD IDOK
      ${EndIf}
    ${EndIf}
  ${EndIf}
!macroend

; Setup runs the uninstaller of the installed version with --updated before it installs a new one.
; That run keeps the desktop shortcut and the user data.
!macro customUnInstall
  ${IfNot} ${isUpdated}
    WinShell::UninstShortcut "$newDesktopLink"
    Delete "$newDesktopLink"
    !insertmacro askToDeleteUserData
  ${EndIf}
!macroend
