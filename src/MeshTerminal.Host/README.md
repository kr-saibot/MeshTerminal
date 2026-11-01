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

## QR-Codes vom Bildschirm lesen

Im Fenstermenü (Alt+Leertaste oder Rechtsklick auf die Titelleiste) und im Tray-Menü stehen „QR-Code aus Bildschirmbereich“ und „QR-Codes auf allen Bildschirmen“ bereit. Der Host blendet sich kurz aus und nimmt den Desktop auf. Bei der Bereichsauswahl einen vollständigen QR-Code einschließlich hellem Rand markieren; Escape bricht ab. Mehrere Monitore und mehrere erkannte Codes werden unterstützt.

Die Vorschau zeigt den erkannten Inhalt vor der Übergabe. „Kopieren“ kopiert ihn; „An MeshTerminal übergeben“ öffnet für bekannte MeshCore-/Meshtastic-Kanal- und Kontaktlinks die vorhandene Importabfrage. Anderer Text kann nach Bestätigung in das Nachrichtenfeld eingefügt werden. Es wird nichts automatisch importiert oder gesendet. Die Übergabe läuft über eine private lokale Verbindung zum zugehörigen Terminalprozess und hängt nicht vom aktiven Eingabefeld ab. Host und Client müssen beide diese Funktion unterstützen und gemeinsam neu gestartet werden.

Bildschirmbilder bleiben im Arbeitsspeicher. Es werden keine Screenshots gespeichert oder hochgeladen. Die Bildschirmaufnahme gehört zum Windows-Host; der eigenständige Linux-Client erhält keine Bildschirmaufnahmefunktion.
