# MeshTerminal Windows host

`MeshTerminal.Host.exe` runs `MeshTerminal.exe` in a native Windows terminal window
using ConPTY and the Windows Terminal renderer. It supports mouse and keyboard
input, Unicode, terminal colours, tray integration, screenshots, selectable icons,
sound settings and optional Windows startup.

Use `MeshTerminal.cmd` in the Windows build root. It resolves both executables
relative to its own location, so the complete output folder can be moved.
Keep the client in the Windows root and the host/dependencies in `Host/` to avoid
assembly-version collisions. The host first checks for the packaged MeshTerminal
client before using a saved fallback or asking for another executable.

Host preferences remain in `Host/settings.ini`. Copies renamed to a different
executable name use their own `<name>.settings.ini`. Existing preferences and
Windows startup registration remain compatible with the former host.

Build all supported outputs from the repository root with:

```powershell
.\tools\Build-All.ps1
```

Canonical outputs are `C:\Users\Tobias\Documents\ConsoleClientBuilds\Windows`
and `C:\Users\Tobias\Documents\ConsoleClientBuilds\Linux`; the host is included
under `Windows/Host`. The script preserves existing settings and databases.
