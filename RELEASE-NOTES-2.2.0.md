# MeshTerminal 2.2.0

## Neu seit 2.1.0

- **Chats:** Über **Insert → Mention person (@)** lassen sich die Absender der letzten Nachrichten auswählen und als `@Name` einfügen. Kanäle und Direktchats können per **M** oder **Mute / Unmute** dauerhaft stummgeschaltet werden. Stumme Chats lösen keine Alerts und kein Ungelesen-Blinken aus. Der Ungelesen-Zähler bleibt sichtbar.
- **Windows-Host:** Terminaltext mit **Umschalt + Ziehen** markieren und mit **Strg + Umschalt + C** kopieren. Verbesserungen beim Zeichnen reduzieren das Cursor-Springen in Eingabefeldern und das Flackern in Dialogen und Kartenansicht.
- **MeshCore-Pfade:** Hin- und Rückwege werden mit Zeitstempel gespeichert. Die Pfadansicht greift auf den letzten bekannten Stand zurück, wenn das Gerät keinen aktuellen Pfad liefert.
- **Meshtastic Traceroute:** Funkpfade aus den Node-Details abfragen, mit wählbarem Kanal und Hop-Limit. Hin- und Rückweg lassen sich mit verfügbaren SNR-Werten und Entfernungen anzeigen. Das letzte Ergebnis bleibt gespeichert und kann auf Kartenlayer 7 eingeblendet werden.
- **MeshCore-Repeater:** Eine bestehende Anmeldung kann wieder aufgenommen werden. Pro Repeater lässt sich eine regelmäßige Statusabfrage mit optionaler Nachbaranzahl aktivieren. Die Werte werden als Telemetrie gespeichert und an konfigurierte Telemetrie-MQTT-Clients weitergegeben.
- **Eigene TX-Auslastung:** Statusleiste, **Device info** und Systemstatus per MQTT zeigen die Sendezeit-Auslastung. Bei Meshtastic stammt der Wert vom Gerät, bei MeshCore wird er aus dem Sendezeitzähler geschätzt.
- **Kompaktere Statusleiste:** `Bat` und `BLE` sparen Platz. Protokollname und TX-Zeitfenster entfallen in der Leiste.
