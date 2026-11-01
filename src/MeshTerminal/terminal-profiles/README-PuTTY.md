# PuTTY: Farben wie im MeshTerminal-Host

MeshTerminal arbeitet mit 16 benannten Terminalfarben. Der Windows-Host verwendet
feste RGB-Werte. PuTTY kann fuer dieselben Farbnamen andere RGB-Werte verwenden;
auch die Behandlung von fettem Text beeinflusst helle Farben. Deshalb koennen
zum Beispiel Blau, Grau und Gelb in verschiedenen Terminals unterschiedlich wirken.

Die automatische PuTTY-Palettenoption wurde entfernt. MeshTerminal sendet keine
Steuersequenzen mehr zum Setzen oder Zuruecksetzen einer Terminalpalette.
Die folgende Vorlage bleibt als freiwillige, manuelle Farbkonfiguration erhalten.

Bei tmux sind insbesondere Dunkelgrau und Weiss von der 256-Farben-Unterstuetzung
abhaengig. Eine Palette allein behebt eine 8-Farben-Terminaldefinition nicht.
MeshTerminal erkennt installierte 256-Farben-Definitionen fuer `screen`/`tmux`.
Fuer die aeussere Sitzung kann `tmux -2 attach -t mesh` verwendet werden.
Eine manuelle Einstellung ist z.B. `set -g default-terminal "tmux-256color"`
in der tmux-Konfiguration (sofern diese Definition installiert ist).

## Farbvorlage fuer PuTTY unter Windows

1. Die Datei `PuTTY-MeshTerminal-colours.reg` auf dem PC mit PuTTY oeffnen und
   importieren. Sie setzt ausschliesslich die Farben und die Farboptionen im
   PuTTY-Profil **MeshTerminal**. Ein vorhandenes Profil dieses Namens behaelt
   seine Verbindungsdaten, bekommt aber die neuen Farbwerte.
2. PuTTY danach oeffnen, unter **Session** das Profil **MeshTerminal** laden.
3. Zielrechner und Verbindungsart einstellen und die Session speichern.
4. Fuer die aktuelle Verbindung das Fenster neu oeffnen oder die Werte unter
   **Change Settings > Window > Colours** uebernehmen.

Es wird nichts automatisch importiert. Andere PuTTY-Profile und Default Settings
werden durch die Vorlage nicht geaendert. Die Vorlage ist fuer das normale
Windows-PuTTY mit Registry-Konfiguration bestimmt.

## Manuell oder unter Linux

Unter **Window > Colours**:

- ANSI colours, xterm 256 colours und 24-bit colour zulassen.
- **Use system colours** ausschalten.
- **Indicate bolded text by changing: The colour** auswaehlen.
- Die ANSI-Farben und ihre Bold-Varianten wie folgt setzen (RGB):

| Farbe | Normal | Bold / hell |
| --- | --- | --- |
| Black | 0,0,0 | 128,128,128 |
| Red | 128,0,0 | 255,0,0 |
| Green | 0,128,0 | 0,255,0 |
| Yellow | 128,128,0 | 255,255,0 |
| Blue | 0,0,128 | 0,0,255 |
| Magenta | 128,0,128 | 255,0,255 |
| Cyan | 0,128,128 | 0,255,255 |
| White | 192,192,192 | 255,255,255 |

Standard-Vordergrund: 192,192,192; heller Vordergrund: 255,255,255;
Standard-Hintergrund und heller Hintergrund: 0,0,0.

Bei SSH nach Linux sollte der Terminaltyp zur Sitzung passen, beispielsweise
`xterm-256color`, sofern auf dem Linux-Rechner eine passende Terminaldefinition
vorhanden ist. Das allein gleicht die RGB-Palette aber nicht an. Schrift,
Kantenglaettung und Bildschirm koennen weiterhin kleine Unterschiede verursachen.

PuTTY-Dokumentation: https://the.earth.li/~sgtatham/putty/0.85/htmldoc/Chapter4.html#config-colours
