# MeshTerminal 2.1.0

Änderungen seit dem letzten veröffentlichten Release **1.4.0**. Veröffentlichungstext vorbereitet am 1. November 2026.

## Neue Funktionen

- **Neuer Name MeshTerminal:** Der bisherige Meshtastic Console Client unterstützt jetzt Meshtastic und MeshCore unter Windows und Linux.
- **MeshCore-Unterstützung:** Direkt- und Kanalnachrichten, Kontaktverwaltung, Adverts mit optionaler Position, Room-Server-Anmeldung, Telemetrie, Geräteinformationen und Synchronisierung der Geräteuhr.
- **Bluetooth LE für beide Protokolle:** Native .NET-Anbindung unter Windows sowie BlueZ/D-Bus unter Linux, ohne Python-Hilfsprogramm.
- **Überarbeiteter Kanaleditor:** Getrennte Listen für Gerätekanäle und lokale Kanäle, Übertragung in beide Richtungen, wählbare Geräteslots, Überschreibabfragen, QR-Codes sowie Kanal- und Kontaktlink-Import. MeshCore-Hashtag-Kanäle werden unterstützt.
- **Erweiterte MeshCore-Kontaktverwaltung:** Anzeige von Gerät-/PC-Speicherung und Node-Typ, Favoriten und Filter; Kontakte auf dem Gerät speichern, anlegen oder entfernen. Fortschrittsanzeige beim Entfernen aller Gerätekontakte.
- **MeshCore-Repeater-Werkzeuge:** Repeater-Suche, Erreichbarkeitsprüfung, Administrator-Anmeldung, Statusabfrage einschließlich Noise Floor, Befehlshilfe, sechs konfigurierbare Schnellbefehle und optional gespeicherte, verschlüsselte Repeater-Passwörter.
- **MeshCore-Pfade:** FROM-/TO-Pfade mit Zwischenstationen und verfügbaren Entfernungen; temporärer Kartenlayer mit farbigen Nodes und Verbindungslinien.
- **MQTT-Clients:** Mehrere Broker mit Benutzername/Passwort, optional TLS und individuellen Basistopics. Zuordnung zu Kanal, einzelnem Direktchat, allen Direktnachrichten, Telemetrie, Adverts/neuen Nodes oder eigenem Systemstatus; passende Favoriten-/Neu-Filter.
- **MQTT-Ausgabe und Senden:** Nachrichtentext, verfügbare Metadaten als Einzel-Topics und vollständiges JSON. Optionales Senden per Checkbox; bei allen Direktnachrichten antwortet das gemeinsame Sende-Topic auf den zuletzt über MQTT ausgegebenen Absender. Retained-Platzhalter machen Sende-Topics sichtbar, ohne selbst Funknachrichten auszulösen.
- **MQTT-Systemstatus:** Geräteinformationen, Verbindung, Batterie und Noise Floor sowie getrennte LoRa-Paket- und PC-/Geräte-Framezähler; sofortige Veröffentlichung nach MQTT-Verbindung und einstellbares Intervall.
- **Telnet-Server:** Mehrere konfigurierbare Kanalbrücken mit lokaler oder Netzwerk-Bindung, optionalem Passwort, Begrüßung und Nachrichtenhistorie; Read-only-Modus, Eingabe-Echo, BEL, anpassbarer Textausgabe und Emoji-Umsetzung.
- **Schach im Direktchat:** Spielstände und Züge als Mesh-Nachrichten, Wiederaufnahme aus dem Verlauf, automatische Aktualisierung, erneutes Senden und vier Darstellungen: Unicode-Symbole, Buchstaben und zwei Blockgrafik-Varianten.
- **Einfügen-Menü im Chat:** Zwischenablage, Emojis und Symbole, eigene Position, Kontakt-/Kanallinks, Datum/Uhrzeit, Karten-Cursorposition und gespeicherte Positionsmarker.
- **Erweiterte Nachrichtenaktionen:** URLs, GPS-Koordinaten, Node-Namen und geteilte Kartenobjekte erkennen; Absenderdetails direkt öffnen, Entfernungen/Richtungen anzeigen, Punkte auf der Karte markieren und gelöschte geteilte Kartenobjekte wiederherstellen. Fehlgeschlagene Nachrichten lassen sich erneut senden.
- **QR-Leser im Windows-Host:** Bildschirmbereich oder gesamten Bildschirm nach QR-Codes durchsuchen; erkannte Inhalte kopieren oder an MeshTerminal übergeben.
- **Kommunikationslog:** Rohdaten und decodierte Geräteframes mit Zeitstempel und farbigem RX/TX, begrenztem RAM-Puffer, LoRa-Filter, Pause, Löschen und Kopieren.
- **Protokolldokumentation im Programm:** Mitgelieferte Markdown-Beschreibungen im Ordner `Protocols`, direkt über das Info-Menü lesbar, unter anderem für MQTT, Kartenaustausch und Schach.

## Verbesserungen

- Stabilere und schnellere Synchronisierung großer Kontaktlisten, besonders über Bluetooth; Datenbankschreibvorgänge werden während des Listenempfangs zurückgestellt. Fortschritt, Wiederholungen und Timeouts werden verständlicher angezeigt.
- MeshCore-Direktnachrichten mit automatischen Wiederholungen, angezeigter Versuchszahl und Flood-Fallback; Empfangsbestätigungen und Sendestatus werden zuverlässiger zugeordnet.
- Verbesserte Absenderzuordnung in Gruppenchats, Direktchat-Aktionen und Positionstelemetrie; Chatreihenfolge bleibt auch bei falschen Absenderuhren nachvollziehbar.
- Kartenobjekte unterstützen Prioritäten, auch beim Teilen. Stadt-Overlays nutzen Bevölkerungswerte zur Priorisierung. Erweiterte Layerauswahl, Altersfilter und Zentrierung auf die eigene Position beim ersten Kartenaufruf.
- Empfangene `[LOC]`-Nachrichten und geteilte Positionsmarker können Positionstelemetrie erzeugen.
- Telegram-Gateways und Bots unterstützen die protokollabhängige Kanalauswahl einschließlich erweiterter MeshCore-Kanalzahlen und sichtbarer Kanalnamen.
- Konfigurierbare Farben für Absender, Zeitstempel und Sendeergebnisse; optionale Logo-Animation und kurzes Aufblinken bei LoRa-bezogenen Frames. Einstellbare Sperrzeit für aufeinanderfolgende Alarmtöne.
- Zahlreiche Korrekturen bei Listen, Scrollbalken, Fokusreihenfolge, Löschdialogen, Zwischenablage und Unicode-Darstellung.
- Portabler Windows-Host mit eigenen Einstellungen pro Host-Verzeichnis, verbesserten Icons und relativem Starter. Windows- und Linux-Releasearchive können per Skript ohne private Laufzeitdaten erzeugt werden.

## Hinweise zum Update

- Programmstart jetzt mit `MeshTerminal.exe`, unter Linux mit `mono MeshTerminal.exe`; der Windows-Starter heißt `MeshTerminal.cmd`. Den Ordner `Host` samt Abhängigkeiten beibehalten.
- Vorhandene Einstellungen und Datenbanken behalten ihre Dateinamen. Eigene Laufzeitdaten beim Aktualisieren erhalten und nicht in öffentliche Releasearchive aufnehmen.
- Meshtastic und MeshCore verwenden getrennte Nachrichten-Datenbanken. Verfügbare Gerätefunktionen und Funkwerte hängen von Protokoll und Firmware ab.
- MQTT- und andere Gateway-Zugangsdaten stehen in der lokalen Einstellungsdatei. Die Lizenz bleibt **GPLv3**.
