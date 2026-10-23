# MeshtasticConsoleClient

MeshtasticConsoleClient is a C# 7.3 / .NET Framework 4.7.2 client library and terminal
application for the native Meshtastic Protobuf API. It connects by USB/serial
TCP or Bluetooth LE, stores messages, nodes and telemetry in SQLite, and supports optional
Telegram gateways and local or HTTP chat bots.

It also includes an initial MeshCore Companion protocol adapter for serial and
TCP and Bluetooth LE connections, with contacts, configured channels, direct chat and channel
messages. Select **MeshCore** in **Settings > Connection**, enter the serial port
(for example `COM14`) and baud rate `115200`, save, and restart the program.
The TCP port must match the MeshCore device's configuration; the Meshtastic
default `4403` is not automatically changed.

MeshCore chat data is stored separately in `meshcore-messages.db`. Full public
keys are mapped to persistent local database IDs without truncating keys.
Group messages identify their sender by a display name, so group senders are
kept separate from authenticated contacts with the same name. The composer
uses the MeshCore UTF-8 text limit, including the local name prefix for groups.
Channel messages become `Sent` after local acceptance; no recipient ACK is available.
Direct messages initially show `QueuedAtDevice` and can subsequently
be marked `Delivered` by a radio ACK. A missing direct ACK is marked `Failed`.

Room Servers appear in the direct chat list. Use **MeshCore > Room Server login**
to enter a password and see whether the server grants read or write access.
Passwords are not saved; log in again after reconnecting. Room posts are limited
to 151 UTF-8 bytes. Signed room messages remain in the room conversation and
show the author name only when its short key identifies a unique contact.

Use **Telemetry > Request MeshCore telemetry** to query the local device or a
contact. Remote devices must permit the request. Received sensor values and raw
payloads are stored when telemetry storage is enabled.
Repeater commands and contact import are not implemented. Telegram gateways,
local chat bots and HTTP bots support both protocols. Positions supplied by MeshCore contacts can appear on
the existing map. Protocol switching requires a restart to select the matching
database. Existing Meshtastic history and settings are retained.

Protocol reference: https://github.com/meshcore-dev/MeshCore/blob/main/examples/companion_radio/MyMesh.cpp

Source code and releases: https://github.com/kr-saibot/MeshtasticConsoleClient

## Bluetooth LE (Windows and Linux)

Bluetooth uses native .NET access on Windows and BlueZ over D-Bus on Linux.
No Python installation or helper process is required.

1. Enable Bluetooth and pair the radio in the operating system's Bluetooth settings.
2. Open **Settings > Connection**, choose the protocol and **Bluetooth LE**.
3. Use **Select device** to choose a paired radio, then save and connect.
   A Bluetooth address can also be entered manually.

Windows requires Windows 10/11 and a BLE-capable adapter. Linux uses the existing
Mono build and requires a running BlueZ service, the system D-Bus, a powered
adapter and permission for the current user to use Bluetooth. The device must
be paired on that Linux computer. Device selection lists paired devices; the
selected protocol's GATT service is validated when connecting.

MeshCore requires **Bluetooth companion firmware**; a device running serial
companion firmware on COM14 may require different firmware before BLE works.
Meshtastic requires Bluetooth to be enabled in its device configuration. Close
any mobile app connected to the same radio before connecting this client.
Automatic reconnect also applies to Bluetooth connections.

Both platform builds and automated packet/disconnect/settings tests have been
verified. Real-radio BLE operation, especially Linux adapter/permission behavior,
still needs a hardware test.
## Screenshots

### Chat

![Chat interface of MeshtasticConsoleClient](Screenshot.png)

### Node map

![Node map with nodes, overlays, grid and distance scale](Screenshot-map.png)

### Node list

![Filterable and sortable list of known Meshtastic nodes](Screenshot-nodes.png)

### Emoji picker

![Keyboard-controlled emoji picker](Screenshot-emoji.png)

## Features

- Native serial and TCP transports with automatic reconnect and connection statistics
- Separately configurable DTR and RTS control lines for serial/COM connections
- Node, channel, text-message, position and telemetry handling
- SQLite message, node and telemetry storage
- Terminal chat interface with direct chats, channels, node details, telemetry and
  message-detail actions for opening the sender, starting a direct chat or locating
  the sender on the map
- Interactive node map with clustering, pan/zoom, GPS centring, XML overlays and
  optional offline raster or OpenMapTiles-compatible vector MBTiles backgrounds
- Selectable OSM roads, buildings and map features with English-name preference
- Stored node-position history with an automatic latest-available 24-hour window
- Optional map hop-limit filter, including hop limit zero for directly received nodes
- Filterable and sortable node list with saved view settings
- Meshtastic receive-time handling, device-clock synchronisation and distinct delivery
  states for direct and channel messages
- Optional Telegram gateways, local chat bots and HTTP bots
- Configurable appearance, logo rotation and emoji replacement/picker support
- Windows desktop host with ConPTY rendering, mouse support and persistent settings
- Bounded Linux shutdown that avoids prolonged Terminal.Gui/Mono exit delays

## Windows desktop host

`MeshtasticConsoleHost.exe` provides a native Windows window for the otherwise
unchanged terminal application. It uses Windows ConPTY and the Windows Terminal
renderer, so keyboard navigation, Tab, cursor keys, mouse input, Unicode symbols
and terminal colours work as they do in a modern terminal.

Keep the release directory structure intact: the client and its dependencies
belong in the Windows output root; the host and its dependencies belong in the
**Host** subdirectory. This prevents dependency-version collisions. The host
locates the packaged client or uses a selected fallback. A different client can
also be selected manually from the window system menu.

The host adds the following Windows integration:

- Configurable terminal font sizes
- Saved window size, font size, dark title bar and selected fallback client
- Optional minimized startup and automatic startup with Windows
- Close button minimizes the window; **Close** in the system menu and
  **Alt+F4** terminate the application
- Taskbar flashing when the client sends a terminal BEL alarm
- Selectable alarm audio with a bundled default alert sound


Host settings are stored in `settings.ini` beside `MeshtasticConsoleHost.exe`.
The ConsoleClient continues to store `meshtastic-settings.xml` and its SQLite
database in the ConsoleClient working directory, which is the directory that
contains the selected `ConsoleClient.exe`.

The host requires 64-bit Windows 10 version 1809 or newer. ConPTY is a Windows
API; it is not an additional separately licensed component.

## Node map

The Node Map displays all nodes with known positions relative to the current GPS
position. It supports keyboard and mouse navigation, zooming, node selection,
clustering, a coordinate crosshair, distance scale and configurable colours.
Additional places and boundary data can be loaded from XML overlay files. Active
overlays and their rendering priority are managed through **Map > Overlays** and
are restored on the next start.

An offline background map can be placed in the `osm` directory beside the
application. Raster MBTiles containing PNG/JPEG tiles and vector MBTiles using
Mapbox PBF tiles are supported. The lightweight vector renderer is designed for
OpenMapTiles-compatible layers including roads, railways, boundaries, buildings,
water and land use. If several `.mbtiles` files are present, the first one in
alphabetical order is opened. Press **8** on the map to enable or disable it.
Detailed creation and validation instructions are included in
`osm/MBTILES-CREATION.txt` and `osm/MBTILES-ERSTELLEN.txt`.

Map colours are cached for the current viewport and vector features use a spatial
index, keeping redraws responsive even with detailed extracts. Grid, crosshair,
scale, nodes, overlays and position-history markers retain the underlying map
colour. Selected foreground markers are displayed with inverted foreground and
background colours. A left click on an unoccupied map position displays the OSM
feature in **Selected Item**. English names are preferred, followed by the
default/local name, German name and road reference. After panning or centring,
the visible node, overlay or history point beneath the crosshair is selected;
otherwise the corresponding OSM feature or the offline map itself is shown.

Keys **1** through **7** toggle configured XML overlays, **8** toggles the
offline background, **9** toggles position history and **0** toggles known nodes.
Position history opened from a node automatically covers the 24 hours ending at
the newest stored position, even when that position is older than one day. Brief
map and layer notifications remain visible for up to four seconds and close
immediately when any key is pressed.

**Map > Hop Limit** enables a hop-count filter independently from its numeric
value. A limit of `0` therefore shows directly received nodes rather than
disabling the filter. Nodes without known hop information are hidden while the
filter is enabled. Node details show both the last-received timestamp and a
readable relative age, for example `(2 days 10 hours ago)`.

### Overlay shapes and measurement

XML overlays support points, lines, circles and rectangles with configurable
colours, fill symbols and fill density. The symbol picker includes the CP437
symbols and additional Unicode symbols; the Windows host preserves these symbols
during terminal output. **Copy current map** includes rendered shapes and markers.

Press **M** repeatedly to cycle measurement through **Off**, **Line**, **Circle**
and **Rectangle**. Temporary shapes are drawn with **M** characters between the
measurement anchor and the cursor. **Selected Item** shows distance and direction
for a line, radius and area for a circle, or width, height and area for a rectangle.
Press **N** to create an item with the current measurement shape; when measurement
is off, a point is selected. Creating the item turns measurement off.

Press **Space** to centre a selected item. **Enter** or a double click on a node
cluster opens a list showing short name, hops, long name and last-heard time.
Select a node to open its details.

### Map sharing

Configure **Map point sharing** and its channel to exchange overlay objects with
other clients. Points, lines, circles, rectangles and their fill settings are
transmitted as ordinary Meshtastic text messages. **Map item details > Send again**
resends the selected object with its existing UUID. Objects already present at the
receiver are deduplicated by UUID; resending does not update an existing object.
Deletion can also be shared.

Messages must fit within 233 UTF-8 bytes; long names or descriptions may need to
be shortened. The format and receiving rules are documented in
[MAP-SYNC-PROTOKOLL.md](MAP-SYNC-PROTOKOLL.md).

### Position markers

**Map > Position Markers** configures the own marker's call sign, free-text info,
colour and centre symbol. Markers look like crosshairs, render above all other map
layers and are stored only in memory. Each Meshtastic node ID has one marker;
incoming updates replace that node's marker.

Press **O** to set the own marker. Settings choose the crosshair or current device
GPS position, whether to ask before setting it, and whether received marker
messages are evaluated. GPS mode requires a valid device position. With map point
sharing enabled, setting the marker also sends it to the configured channel.

Press **L** to open the narrow marker list near the right edge. It shows call sign,
distance and update age. Moving through the list centres and selects each marker;
**Enter** or a double click opens its info box. Details include free text, update
age, distance and direction relative to the own marker, or device GPS if no own
marker exists. Actions centre the map, open node details, start a direct chat or
delete the marker locally after confirmation. A later received update can recreate
a deleted marker. Markers disappear when the application exits.

### Included Germany overlays

- Federal-state boundaries use red asterisk (*) lines, including island outlines.
- The water and city overlay contains blue **w** river and lake outlines,
  brown filled **.** circles for major cities and green points for smaller places.
  City-circle radii approximate municipality area, rather than built-up area.
  Autobahns are not included.

See the [water and city overlay documentation](src/ConsoleClient/map/germany-water-cities-motorways.md)
for coverage, approximation details, sources and licences. Its historical filename
is retained for compatibility with saved overlay settings.

## Messages and delivery status

Incoming text messages use the Meshtastic packet `rx_time` as their displayed
receive time when firmware supplies it, with the PC receive time used only as a
fallback. The client sets the connected device clock early in the connection so
that subsequently received packets can contain a useful receive timestamp.

The message-details window can open the sender's node details, start a direct
chat, or centre and select the sender on the map. When the messages window gains
focus, it selects the timestamp of the newest message automatically.

Direct messages remain **Queued at device** until the corresponding acknowledgement
arrives and then change to **Delivered**. Channel broadcasts cannot receive an
individual delivery acknowledgement, so a successfully queued channel message is
shown as **Sent** instead of remaining indefinitely at **Queued at device**.

On Linux, shutdown uses bounded waits for background services and queued database
writes, restores the terminal and then exits directly. This prevents the
Terminal.Gui/Mono event loop from consuming CPU for minutes or hours after a
long-running session.

### Alerts

**Settings > Alerts > Suppress alerts for ///MAP messages** excludes map-object,
deletion and position-marker messages from desktop notifications, Windows beeps,
terminal BEL, repeated beeps, unread-message logo blinking, alert HTTP requests
and alert shell commands. Messages remain in chat and map synchronisation remains
active. The option is off by default.

## Emoji picker

The emoji picker provides keyboard-friendly selection and preview of emoji in a
terminal. Open it with **F6**, navigate with the cursor keys and insert the
selected emoji with **Enter**. Where a terminal cannot render an emoji directly,
the client can use generated ASCII representations or configured replacements.

## Build

Prerequisites:

- .NET SDK with .NET Framework 4.7.2 targeting support
- NuGet package restore access

```powershell
./tools/Build-All.ps1
```

The script builds both platforms, runs MeshCore protocol tests against a simulated
Companion radio, and updates `C:\Users\Tobias\Documents\ConsoleClientBuilds\Windows`
and `C:\Users\Tobias\Documents\ConsoleClientBuilds\Linux`. Existing runtime data
is preserved. The Windows host and its dependencies are in `Windows\Host`.
The `Windows\MeshtasticConsoleHost.lnk` shortcut starts the native terminal.
Temporary build and test files are isolated in `.build-staging`.
The Windows host project is located in `src/ConsoleHost/` and targets x64.
Release builds do not include `.pdb` debug-symbol files. These symbols are not
needed at runtime; use a Debug build when source-level crash diagnostics are
required.
On Linux it is intended to run with Mono and uses the distribution-provided
`libsqlite3.so` library.

After building, an optional read-only hardware probe can be run with
`.\.build-staging\Tests\MeshCore.Tests.exe --port COM14`. It reads device
information, contacts and channels, without transmitting radio messages or
consuming queued messages. Add `--telemetry` to also read local sensor values.
Close other applications using that serial port first.

## Editing device channels

Use **Settings > Edit channels** after connecting and synchronizing the device.
The editor reads every slot, including free ones. Select a slot to edit its name,
key and enabled state, then choose **Save to device**. Keys are hidden until
**Show key** is selected. **New random key** generates a private channel key.
Other participants need the same name and key to communicate.

Meshtastic supports names up to 11 UTF-8 bytes and keys of 0, 1, 16 or 32 bytes.
Enter keys as hex or with a `base64:` prefix. An empty key disables encryption;
`01` (also available through **Default key**) selects the public default key.
The editor also supports MQTT uplink/downlink, mute and position precision.
The existing primary channel stays primary; other slots can be enabled as
secondary channels or disabled. Settings not exposed by the editor are retained.

MeshCore supports names up to 31 UTF-8 bytes and 16-byte keys (32 hex characters).
Disabling a slot clears its name and key. Existing slot numbers are preserved.
Saving waits for the device reply and reads the slot back before reporting
success. If a write cannot be confirmed, close and reopen the editor to reload
its current state. Only the locally connected device is edited.

**Show QR code** shares the current editor values, including the key, without
saving them to the device. Scan it in the matching mobile app. Meshtastic uses
an add-only single-channel URL without LoRa configuration; MeshCore uses its
channel-import URI. **Save PNG** exports a scannable image. If the terminal is
too small to show the whole QR, enlarge it or use the exported PNG.

Use **MeshCore > Send advert** to announce a MeshCore device. It supports
zero-hop adverts (directly reachable devices) and flood adverts (forwarded
through the mesh). **Include the device position** controls whether this advert
contains the position currently stored on the device. The original advert
position policy is restored afterwards, including after a rejected send.
Meshtastic uses **Nodes > Broadcast node information** to send local NodeInfo on its
primary channel. A successful submission does not confirm reception elsewhere.
The status line shows the local long name, with the short name as a fallback.

## Running

Start `ConsoleClient.exe`. On its first start, enter the serial or TCP settings
through the Settings menu. The application writes local settings and its SQLite
database next to the executable; these files can contain private information
and are deliberately excluded from version control.

### Serial/COM control lines (DTR and RTS)

The connection settings provide separate **DTR enabled** and **RTS enabled**
options for serial/COM-port connections. The defaults are DTR enabled and RTS
disabled. Changes take effect on the next connection and do not affect TCP.

Some ESP32-S3 boards and USB serial circuits, including certain Heltec WiFi LoRa
32 V3 configurations, connect DTR and RTS to reset or boot-control signals. If a
device resets, appears to shut down, or loses its clock when ConsoleClient closes
the port, disable both DTR and RTS and reconnect. Other boards or drivers may need
DTR to communicate, so keep the default unless the device exhibits this problem.

### Linux serial-port permissions

On Ubuntu and many other Linux distributions, serial devices such as
`/dev/ttyACM0` and `/dev/ttyUSB0` are accessible only to members of the device's
group, which is usually `dialout`. If TCP works but serial connections fail for
both nRF and ESP32 devices, check the port and current group memberships:

```bash
ls -l /dev/ttyACM0
ls -l /dev/ttyUSB0
groups
```

If the port belongs to `dialout`, add the current user to that group:

```bash
sudo usermod -aG dialout "$USER"
```

Log out completely and log in again, or restart the computer, before retrying.
Use `groups` to confirm that `dialout` is active. Do not run ConsoleClient with
`sudo`; correct the device-group permissions instead. Also ensure that no web
client or other application still has the selected serial port open.

## Appearance and logos

The terminal colours, frames and message colours can be changed in the
Appearance settings. ASCII logos are loaded from `src/ConsoleClient/logo/` and
can be replaced or extended with your own text files. The Logo settings control
whether the logo frame is shown, its size and the automatic rotation interval.

The emoji replacement list can be regenerated from official Unicode data:

```powershell
cd src\ConsoleClient
powershell -ExecutionPolicy Bypass -File .\tools\GenerateEmojiReplacements.ps1
```

## Security and privacy

Never commit `meshtastic-settings.xml`, SQLite databases, logs, Telegram bot
tokens, chat IDs or other credentials. If a token is exposed, revoke it at
BotFather immediately and create a replacement token before publishing.

## Credits

Created by Tobias Krista with assistance from artificial intelligence
(OpenAI Codex).

## License

Copyright (c) 2026 Tobias Krista.

This project is licensed under the **GNU General Public License, version 3.0
only (GPL-3.0-only)**. See [LICENSE](LICENSE). The Meshtastic Protobuf
definitions included in this repository are also GPL-3.0; their original
license copy is retained in `LICENSE-Meshtastic-Protobufs.txt`.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for third-party component
attribution and release-packaging notes.
