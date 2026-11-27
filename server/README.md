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
| `-data` / `COOP_DATA` | `coop-data.json` | File storing legendary owners and badges (back it up!) |
| `-legendaries` | built-in list | Comma-separated National Dex numbers the registry tracks |

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

## Legendaries: one of each per room (0x20–0x24)

Each room code has its own registry, saved in the data file so it survives restarts. Ownership is tied to a player's **identity**: name + trainer ID + secret ID, sent in the HELLO_REQ extension. Without IDs, the name alone is used.

| Type | Name | Dir | Payload |
|---|---|---|---|
| 0x20 | LEGEND_RESERVE | C→S | species u16. Send when a legendary encounter is about to start |
| 0x21 | LEGEND_RESERVE_ACK | S→C | species u16, status u8, holder_name[24] |
| 0x22 | LEGEND_RESULT | C→S | species u16, result u8: 0 caught, 1 escaped (fled/fainted/lost), 2 released from box |
| 0x23 | LEGEND_UPDATE | S→C | species u16, state u8: 0 free, 1 reserved, 2 owned, owner_name[24]. Full list sent after HELLO, then on every change |
| 0x24 | LEGEND_TRANSFER | C→S | species u16, to_player_id u16. Owner sends after an in-game trade completes |

**ACK status:** 0 granted (start the battle), 1 owned by someone else, 2 someone is battling it now (block the encounter for both), 3 not a tracked legendary (go ahead), 4 you already own it.

**Rules:**
- A reservation lasts until the battle result, at most 15 minutes.
- If the holder disconnects, the reservation is kept for 60 s in case they reconnect, then freed.
- Only the reservation holder can report caught or escaped.
- Only the owner can release or transfer.

## Battle lobbies: team gyms, Elite Four, Champion (0x30–0x3C)

**Gyms and the Elite Four are solo or duo** (max 2 players). The Champion and free battles allow up to 4. The server doesn't simulate battles. It keeps every game in **lockstep**: all games get the same RNG seed and slot order, every turn each game sends its player's chosen action, and the server relays all of them together once everyone has acted.

| Type | Name | Dir | Payload |
|---|---|---|---|
| 0x30 | BATTLE_CREATE | C→S | kind u8 (0 gym, 1 Elite Four, 2 Champion, 3 free), target u16 (gym 1–8 / E4 1–4 / 1), max_players u8 |
| 0x31 | BATTLE_LOBBY | S→C | lobby_id u16, kind u8, target u16, host u16, state u8 (0 open, 1 battling), max u8, count u8, count×(player_id u16, ready u8) |
| 0x32 | BATTLE_INVITE | both | lobby_id u16, player_id u16 (C→S invitee / S→C inviter), kind u8, target u16 |
| 0x33 / 0x34 | BATTLE_JOIN / LEAVE | C→S | lobby_id u16 |
| 0x35 | BATTLE_READY | C→S | lobby_id u16, ready u8 |
| 0x36 | BATTLE_START | C→S host: lobby_id · S→C: lobby_id u16, seed u64, count u8, count×player_id u16 (slot order) |
| 0x37 | BATTLE_ACTION | C→S | lobby_id u16, turn u16 (starts at 1), data[≤512] (opaque to the server) |
| 0x38 | BATTLE_TURN | S→C | lobby_id u16, turn u16, count u8, count×(player_id u16, len u16, data). **Empty data = that player timed out; use a default action** |
| 0x39 | BATTLE_RESULT | C→S | lobby_id u16, outcome u8 (0 win, 1 loss). Host only |
| 0x3A | BATTLE_END | S→C | lobby_id u16, outcome u8, count u8, count×player_id u16 |
| 0x3B | BATTLE_ERROR | S→C | lobby_id u16, code u8: 1 no lobby, 2 full, 3 other room, 4 already in a lobby, 5 not host, 6 not all ready, 7 already started, 8 wrong turn, 9 not a member, 10 bad request (e.g. 3-player gym or Elite Four) |
| 0x3C | PROGRESS | S→C | count u8, count×(kind u8, target u16). Sent after HELLO and whenever you earn a new win |

**Rules:**
- Start needs every member ready, and only the host can start.
- A turn goes out when all members have acted, or after 90 s with empty actions for the missing players.
- If someone disconnects mid-battle, they're dropped and the rest continue. Their slot becomes AI-controlled on the clients.
- On a win of a gym, Elite Four member or Champion, **everyone still in the lobby** gets that achievement saved, and a PROGRESS message is sent to them. Badges follow the player across rooms.

## Layout

```
server/
  cmd/coop-server/     the server binary
  cmd/coop-sim/        mock players: circles across zones 100/101 with zone hops
  internal/coop/       rooms, zones, tick loop, reaper (single hub goroutine, no locks)
  internal/mockclient/ tiny protocol client used by tests and the sim
  internal/store/      JSON persistence for legendary owners and badges
  protocol/            wire format (reusable reference for the C++ client)
  deploy/              systemd unit
```
