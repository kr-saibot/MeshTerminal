# MeshTerminal MQTT gateway

MeshTerminal publishes received Meshtastic or MeshCore messages and telemetry to configurable MQTT brokers (MQTT 3.1.1 over TCP, optionally TLS 1.2 with normal certificate validation). This is a MeshTerminal application gateway, not the Meshtastic native encrypted MQTT radio-packet protocol. It does not change the project GPLv3 license; MQTTnet is MIT licensed.

## Configuration

Settings → MQTT clients opens an overview with Add, Edit, Delete and Status. Each protocol has its own clients. Every client can configure name, enabled state, server hostname/IP, port, TLS, username, password, base topic, binding and Favorites only. Defaults: disabled, localhost:1883, base topic `meshterminal`, sending disabled. TLS brokers normally use port 8883; set the broker's actual port explicitly.

Bindings:

- Channel: all received text messages in the selected named device channel (all available MeshCore slots, eight Meshtastic slots).
- Direct chat: received messages from the selected contact, identified by its full MeshCore public key or Meshtastic ID.
- All direct messages: received direct messages from any sender; replies via the common send topic go to the sender of the latest direct message published through this client.
- Adverts / new nodes: live MeshCore adverts (both short known-contact and full new-contact adverts), or live Meshtastic NodeInfo announcements/newly discovered nodes. Favorites only and Only newly discovered nodes are available. Initial contact synchronization, path-only changes, own-device announcements and ordinary chat/signal updates do not produce these events. “New” means not previously known to the current device-client session. This binding is receive-only.
- System status: the own device information, connection, battery, radio and packet statistics. Published immediately on MQTT connect/reconnect and periodically; the interval is configurable from 5 to 3600 seconds, default 30. Receive-only; favorites/new-node filters do not apply.
- Telemetry: device/environment metrics, positions, received LOC/shared position markers and MeshCore repeater status. Available fields depend on the device. Telemetry export is independent of the telemetry database storage switches; historic database contents are not replayed.

Favorites only filters incoming messages/telemetry by sender. For MeshCore channel messages with a name-only sender, a unique known contact with that name is used; ambiguous names are not assumed to identify a favorite. When sending a direct message, the target must also be a favorite if this option is enabled. The checkbox does not restrict sending to a channel.

Use a distinct base topic for each active client connected to the same broker. Sending the same command to several configured routes can cause several radio messages. The editor rejects duplicate active broker/port/base-topic combinations in this protocol.

Passwords are masked in the editor and kept in `meshtastic-settings.xml`, like other gateway credentials. Protect that file. It is excluded from release archives. No credentials are included in published event JSON or status errors. TLS validates the broker certificate using the operating system trust store.

## Publications: radio → MQTT

`<base>/message` contains the received text, or the raw readable telemetry for a telemetry event. `<base>/json` contains the whole event with metadata as one UTF-8 JSON object. Individual metadata fields are published as subtopics, using `/` for JSON nesting, for example:

| Topic below the base | Value |
| --- | --- |
| `event_id` | Event UUID; stable when that event is retried after a broker disconnection |
| `protocol` | `MeshCore` or `Meshtastic` |
| `kind` | `Channel`, `Direct` or `Telemetry` |
| `direction` | `incoming` (chat messages) |
| `timestamp` | Sender/device timestamp when available; ISO 8601 UTC |
| `received_at` | UTC reception/storage time, independent of sender clock |
| `sender/id` | Full contact ID/public key, when known |
| `sender/number` | Internal numeric node identity (MeshCore numbers are local database IDs) |
| `sender/name` | Sender/contact display name, when known |
| `recipient/id`, `recipient/number` | Destination, when known |
| `channel/index`, `channel/name` | Channel metadata, when applicable |
| `packet_id` | Radio message identifier, when available |
| `radio/hops`, `radio/rssi_dbm`, `radio/snr_db` | Available reception metadata |
| `latitude`, `longitude` | Optional message location |
| `raw_metadata` | Additional original message metadata, when available |
| `type` | Telemetry type |
| `telemetry/battery_percent`, `telemetry/voltage` | Battery metrics |
| `telemetry/channel_utilization`, `telemetry/air_util_tx` | Radio utilization |
| `telemetry/uptime_seconds` | Device uptime |
| `telemetry/temperature_c`, `telemetry/relative_humidity`, `telemetry/barometric_pressure_hpa` | Environment metrics |
| `telemetry/latitude`, `telemetry/longitude`, `telemetry/altitude_m` | Position |
| `telemetry/noise_floor_dbm`, `telemetry/rssi_dbm`, `telemetry/snr_db` | Repeater radio status |

Unavailable values are omitted; zero is a valid value. Publications have QoS 1 and are not retained. Individual topics are separate publications, not an atomic update. Do not combine old individual fields into a new event; consume `/json` for a consistent full snapshot. A field present in an earlier event may be absent in a later one. Own outgoing chat messages and own device echoes are not exported, avoiding MQTT→radio→MQTT loops.

Example `/json`:

```json
{"event_id":"f7c34376-9e37-4617-9e30-6cd6c16f5a86","protocol":"MeshCore","kind":"Channel","direction":"incoming","timestamp":"2026-10-29T12:00:00.0000000Z","received_at":"2026-10-29T12:00:02.0000000Z","sender":{"number":42,"id":"<full-public-key>","name":"Mobile"},"channel":{"index":39,"name":"JMBR"},"radio":{"hops":2,"snr_db":5.25},"message":"Hello"}
```

## Commands: MQTT → radio

The **Allow sending via MQTT** checkbox is off by default. Only enabled channel/direct bindings can subscribe to commands. Telemetry bindings cannot send chat messages.

| Binding | Command topic | UTF-8 payload |
| --- | --- | --- |
| Channel | `<base>/send` | Plain message text to the configured channel |
| Direct chat | `<base>/send` | Plain message text to the configured contact |
| All direct messages | `<base>/send` | Plain reply to the sender of the latest published direct message |

For all-direct routes, the destination is automatically taken from the last received direct message published through this MQTT client. There is no arbitrary target-ID topic. Before a message is published, after reconnect, or when its sender has no known contact ID, sending is rejected rather than replying to a previous/different contact. The destination is captured when a command arrives, so subsequent incoming messages cannot redirect a queued reply. Unknown nodes, non-favorites when restricted, own-device destinations, unavailable device channels and disconnected mesh devices produce a status error and discard the command. An accepted radio send also appears in the normal chat history, with the existing delivery-status handling and MeshCore retries.

Example (channel binding): publish `Hello from MQTT` to `myhome/mesh/send`.

Example (all-direct binding): after receiving a direct message from Mobile, publish `Hello mobile` to `myhome/direct/send` to reply to Mobile. A later received message from another contact changes the reply destination.

On every connection (including reconnect), send-enabled clients publish the retained placeholder `{"meshterminal":"send-placeholder"}` to their send topics. All send-enabled chat bindings create only `<base>/send`. All-direct bindings remove legacy retained `<base>/send/<node-id>` entries for currently known contacts; stale entries for contacts no longer known can be removed in the broker. No per-contact send topics are created or subscribed. Sending-disabled clients and telemetry bindings do not create command topics. The reserved placeholder is always ignored by MeshTerminal, even when received as a live broker echo. It is nonempty because a zero-byte retained message deletes a retained topic under MQTT 3.1.1. Existing retained commands on these topics are replaced with the placeholder at connection; obsolete per-contact topics for unknown/deleted contacts are not automatically deleted.

Do not retain actual commands. Retained subscription replays are ignored. Send-topic subscriptions request QoS 0 to avoid automatically replaying radio commands through broker redelivery. There is no application-level exactly-once guarantee: publishing the same command twice deliberately sends twice. Payloads must be nonempty valid UTF-8 without NUL, at most 4096 bytes; the lower protocol/device text-size limit is also enforced without truncation. Outbound requests are processed sequentially per client. A stopped/reconfigured client discards unsent commands; a device write already started is allowed to finish.

## Status, queues and reconnect

Debug → MQTT client status (also Status in the client overview) shows connection state, published event count, pending exports, successful device-send submissions, dropped items and the last error. “Sent to mesh” means submitted to the own device, not a confirmed remote delivery. Routine broker failures stay in the status view instead of opening popups.

Each enabled client reconnects independently after three seconds with a ten-second broker-operation timeout. It has an in-memory FIFO of up to 500 received events and 32 commands; new arrivals beyond the limit are discarded and counted. Existing pending events are retried after reconnect, preserving event IDs. QoS 1 and interrupted event batches can produce duplicates; consumers can deduplicate by event ID. No queue contents are written to disk. Restarting, editing or disabling a client clears its queue. Database/device callbacks do not wait for broker I/O.

MQTT settings and behavior are shared by the Windows and Mono/Linux builds. The project ships this document in `Protocols`; it can also be opened through Info → Protocols.

## Advert and new-node events

Select **Adverts / new nodes** in the MQTT client editor; optionally enable **Only newly discovered nodes** and/or **Favorites only**. These events follow the same text, individual subtopic and JSON publication scheme. `kind` is `Advert` for MeshCore and `NodeInfo` for Meshtastic. `is_new` is a JSON boolean. `received_at` is the PC reception time in UTC. Node data is under `node`: `number`, `id` (full contact identity), `name`, `short_name`, `type`, optional MeshCore `type_code`, hardware (Meshtastic), available latitude/longitude/altitude and battery percentage. Available reception fields are under `radio`: hops, RSSI and SNR. Unavailable metadata is omitted. MeshCore short adverts are reported after their contact/path metadata has been refreshed, so the MQTT event contains the updated contact name, location and available radio data. A metadata refresh failure can delay or prevent reporting of a key-only advert. No send topic is created for this binding.

## System status

Select **System status** and set **Status interval (s)** (default 30 seconds). `/message` contains the full readable Device info text; `/json` contains the complete structured snapshot. It is published immediately after MQTT connect/reconnect and at the configured interval, without a historic backlog. No send topics are created. Standard QoS 1, non-retained event publication applies.

The snapshot includes `kind: SystemStatus`, `timestamp`, `connected`, and these groups:

- `device`: name, full ID, local number, hardware, firmware/build, MeshCore protocol version, own coordinates when available.
- `connection`: state, transport, endpoint, configured target, connected-since time, current/total duration, reconnect count, `rx_frames`, `tx_frames`, last RX/TX times. These frame counters count PC-to-device communication, not LoRa packets.
- `battery`: voltage, available percentage (0–100), external-power flag and state. A Meshtastic battery value above 100 means external power; MeshCore normally reports voltage only.
- `clock`: MeshCore device time last read, time of that read and synchronization status. Device time is a recorded read, not a continuously polled/live clock.
- `radio`: frequency, bandwidth, spreading factor, coding rate, TX power, MeshCore routing-hash byte length; Meshtastic region, modem preset/custom settings, frequency slot/automatic mode, TX enabled and hop limit; `noise_floor_dbm`, `rx_packets`, `tx_packets`, `rx_errors`, and `statistics_received_at` when available.

Available metadata is also flattened to individual topics, for example `<base>/battery/voltage`, `<base>/radio/noise_floor_dbm` and `<base>/radio/rx_packets`. Radio counters are the device/firmware cumulative LoRa counters, normally since device boot, and include packets not involving this application. They are separate from connection frame counters. MeshCore packet counters use the official GET_STATS command 56, subtype 2, when an enabled system-status client is configured (supported companion protocols). The existing local-stat polling also supplies noise floor and battery voltage. Meshtastic counters/noise are taken only from the own node's LocalStats telemetry. Unsupported/unreceived values are omitted rather than reported as zero.

Publishing status reads cached device information and does not start node/channel synchronization or request remote telemetry. While disconnected, `connected` is false and other fields can reflect the last received values; check the statistics timestamp and connection state for freshness. Broker disconnection does not accumulate obsolete status samples: a fresh snapshot is built after reconnect. The project GPLv3 remains unchanged.
