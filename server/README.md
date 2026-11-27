# Co-op server

The N-player shared-world relay described in §6 of the architecture guide. It's a single Go binary with no dependencies, and it implements the binary protocol from §6.5, rooms and zone-based interest management from §6.3, and the edge cases from §6.6.

> **Status:** the server and a mock-player simulator are done and tested. The in-game client (the subsdk2/subsdk3 network thread, Phases 2–3) is **not** in this folder. Until that exists, the server has no game to talk to, so `coop-sim` stands in for players.

## Quick start (local test)

```sh
cd server
go run ./cmd/coop-server                       # listens on :4100
go run ./cmd/coop-sim -players 4 -duration 10s # in another terminal
go test -race ./...
```

## Hosting it online (US + India group)

Rent a small Linux VPS. The server uses almost no CPU or RAM, so the cheapest plan from any provider is plenty.

**Pick a European region** (Frankfurt, London or Amsterdam). From the US Midwest and from India, Europe is roughly in the middle, so everyone gets similar ping. A US server would give the India player much higher ping than everyone else. Overworld walking tolerates ~150–250 ms fine, because the client interpolates.

1. Build for Linux on your own machine: `GOOS=linux GOARCH=amd64 go build -o coop-server ./cmd/coop-server`, or build on the VPS with Go installed.
2. Copy it to the VPS: `scp coop-server user@VPS_IP:`
3. Install it as a service: see `deploy/coop-server.service`. Set a password with `COOP_PASSWORD`.
4. **Open TCP port 4100** in the VPS firewall and in the provider's network/security-group settings.
5. Everyone puts the VPS's **IP address** (not a hostname; the client's `InetAton` only parses IPs) into `sdmc:/config/pokemon-coop.ini`. In Ryujinx, `sdmc:/` is the folder opened by **File → Open Ryujinx Folder → sdcard**.

Docker alternative: `docker build -t coop-server . && docker run -d --restart unless-stopped -p 4100:4100 -e COOP_PASSWORD=changeme coop-server`

## Options

| Flag / env | Default | Meaning |
|---|---|---|
| `-listen` / `COOP_LISTEN` | `:4100` | TCP listen address |
| `COOP_PASSWORD` | *(off)* | Required password for HELLO_REQ |
| `-max-players` | 4 | Players per room |
| `-max-rooms` | 16 | Simultaneous rooms |
| `-max-conns` | 64 | Open TCP connections |
| `-tick` | 20 | WORLD_SNAPSHOT rate (Hz) |
| `-timeout` | 5s | Evict clients silent this long |
| `-v` | off | Verbose logs (zone changes, rejects) |

## Protocol notes for the client developer

The guide's §6.5 table is implemented exactly. Sizes are checked by `protocol/protocol_test.go`: HELLO_REQ 52, HELLO_ACK 8, PLAYER_META 38, ZONE_CHANGE 24, PLAYER_STATE 28, WORLD_SNAPSHOT 9 + 30·n, PLAYER_LEAVE 3, PING/PONG 4. Little-endian throughout. The server **closes the connection** on a bad magic number, payload_len > 4096, or any non-PING packet before HELLO.

These are additions where the guide was silent:

- **Password:** append `password[16]` (UTF-8, zero-padded) after `initial_zone_id`, so HELLO_REQ becomes 68 bytes. If you leave it off, 52-byte hellos still work when the server has no password.
- **HELLO_ACK status:** 0 OK, 1 room full, 2 bad password, 3 protocol version mismatch, 4 server full (max rooms), 5 bad room code, 6 already in a room. On anything but 0 (and 6), the server closes the connection.
- **Room codes:** case-insensitive, `A-Z 0-9 - _`, up to 16 bytes. Empty means `DEFAULT`. A room is created on first join and deleted when it empties.
- **Player IDs** are per room, start at 1, and never use 0 or 0xFFFF.
- **Resync after battle/map load (§6.6):** send ZONE_CHANGE with `new_zone_id` equal to your current zone. The server resends PLAYER_META for everyone in that zone.
- **Zone desync guard:** a PLAYER_STATE whose `zone_id` differs from your recorded zone counts as a ZONE_CHANGE.
- **WORLD_SNAPSHOT** is only sent when ≥1 *other* player in your zone has sent a position. It never includes you.
- **PLAYER_LEAVE:** reason 0 goes only to peers in the zone you left. Reasons 1 and 2 go to the whole room, so a world-map HUD can update.
- **PONG** echoes the PING's `timestamp_ms`, so RTT = now − echoed value. The server doesn't ping clients: the client must send *something* at least every 5 s (PLAYER_STATE or PING every 2 s, per §6.6).
- **COOP_EVENT:** on client → server, `target_player_id` is the recipient, or `0xFFFF` for the whole room. On server → client, the same field holds the **sender's** ID. The server limits it to 10/s per client and doesn't interpret `data`.
- **Rate limit:** 60 packets/s sustained (burst 120). Packets over that are dropped, and sustained flooding disconnects the client.
- Floats that are NaN, infinite or have |v| ≥ 1e6 are rejected. Names are sanitized (control characters stripped).

## Layout

```
server/
  cmd/coop-server/     the server binary
  cmd/coop-sim/        mock players: circles across zones 100/101 with zone hops
  internal/coop/       rooms, zones, tick loop, reaper (single hub goroutine, no locks)
  internal/mockclient/ tiny protocol client used by tests and the sim
  protocol/            wire format (reusable reference for the C++ client)
  deploy/              systemd unit
```
