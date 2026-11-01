# MeshCore direct-message delivery in MeshTerminal

Automatic retries apply to normal MeshCore direct text messages (including structured messages such as chess snapshots). Channel messages, repeater CLI commands and the explicit reachability probe do not use this retry policy. Meshtastic delivery behavior is unchanged.

- An initially known route allows at most five transmissions: the initial transmission and three retries over the available route, then a final transmission after resetting the destination path to flood.
- An initially unknown route allows at most three transmissions, initially via flood. A newly learned route may be used by subsequent attempts.
- Each transmission waits for the firmware-reported estimated ACK timeout (bounded to 1–300 seconds). A missing radio ACK triggers a retry; a rejected device command or lost connection ends the sequence instead.
- The normal Companion send command remains `0x02`, text type `0`, with retry byte `0` through `4`. Timestamp, destination key prefix and text remain unchanged for one logical message. A path reset uses command `0x0D` and the full 32-byte destination public key.
- Any matching ACK from an active attempt completes the logical message and stops remaining retries. Unrelated ACKs do not count. Disconnect/cancellation also stops retries; an unfinished send is not resumed after restarting the program.
- The radio send response supplies the actual route mode, expected ACK and estimated wait. MeshTerminal displays and persists `Attempt n/max | Direct/Flood` alongside delivery status, without inserting another chat message per retry.
- New MeshCore direct text is limited to 158 UTF-8 bytes so the firmware's fifth-attempt extension has its two required bytes. Room server posts retain their lower limit of 151 bytes. Channels retain their channel-specific size limit.

A missing ACK does not prove the recipient failed to receive the message: the return path may be interrupted. Automatic retries therefore cannot guarantee exactly-once display on every third-party recipient. Firmware and application duplicate handling remain relevant.
