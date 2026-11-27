// Package protocol implements the BDSP co-op binary wire protocol (guide §6.5).
//
// Every packet is an 8-byte little-endian header followed by a payload:
//
//	magic u16 = 0x4243 ("BC") | version u8 = 1 | msg_type u8 | payload_len u16 | seq u16
package protocol

import (
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"math"
	"strings"
	"unicode"
	"unicode/utf8"
)

const (
	Magic      uint16 = 0x4243
	Version    uint8  = 1
	HeaderSize        = 8
	MaxPayload        = 4096
)

type MsgType uint8

const (
	MsgHelloReq      MsgType = 0x01
	MsgHelloAck      MsgType = 0x02
	MsgPlayerMeta    MsgType = 0x03
	MsgZoneChange    MsgType = 0x04
	MsgPlayerState   MsgType = 0x05
	MsgWorldSnapshot MsgType = 0x06
	MsgPlayerLeave   MsgType = 0x07
	MsgPing          MsgType = 0x08
	MsgPong          MsgType = 0x09
	MsgCoopEvent     MsgType = 0x10
)

func (t MsgType) String() string {
	switch t {
	case MsgHelloReq:
		return "HELLO_REQ"
	case MsgHelloAck:
		return "HELLO_ACK"
	case MsgPlayerMeta:
		return "PLAYER_META"
	case MsgZoneChange:
		return "ZONE_CHANGE"
	case MsgPlayerState:
		return "PLAYER_STATE"
	case MsgWorldSnapshot:
		return "WORLD_SNAPSHOT"
	case MsgPlayerLeave:
		return "PLAYER_LEAVE"
	case MsgPing:
		return "PING"
	case MsgPong:
		return "PONG"
	case MsgCoopEvent:
		return "COOP_EVENT"
	}
	return fmt.Sprintf("0x%02x", uint8(t))
}

// HELLO_ACK status codes. 0..1 come from the guide; the rest are server extensions.
const (
	StatusOK            uint8 = 0
	StatusRoomFull      uint8 = 1
	StatusBadPassword   uint8 = 2
	StatusBadVersion    uint8 = 3
	StatusServerFull    uint8 = 4
	StatusBadRoomCode   uint8 = 5
	StatusAlreadyInRoom uint8 = 6
)

// PLAYER_LEAVE reasons.
const (
	LeaveZoneExit   uint8 = 0
	LeaveDisconnect uint8 = 1
	LeaveTimeout    uint8 = 2
)

// BroadcastTarget as COOP_EVENT target sends the event to everyone else in the room.
const BroadcastTarget uint16 = 0xFFFF

// PLAYER_STATE flag bits.
const (
	FlagInBattle   uint8 = 1 << 0
	FlagInMenu     uint8 = 1 << 1
	FlagMegaActive uint8 = 1 << 2
)

var (
	ErrBadMagic = errors.New("protocol: bad magic")
	ErrTooLarge = errors.New("protocol: payload too large")
	ErrShort    = errors.New("protocol: payload too short")
	ErrBadFloat = errors.New("protocol: non-finite or out-of-range float")
)

type Header struct {
	Version uint8
	Type    MsgType
	Len     uint16
	Seq     uint16
}

// ReadPacket reads one packet. buf must have capacity for MaxPayload bytes;
// the returned payload aliases buf and is only valid until the next call.
func ReadPacket(r io.Reader, buf []byte) (Header, []byte, error) {
	var hb [HeaderSize]byte
	if _, err := io.ReadFull(r, hb[:]); err != nil {
		return Header{}, nil, err
	}
	if binary.LittleEndian.Uint16(hb[0:2]) != Magic {
		return Header{}, nil, ErrBadMagic
	}
	h := Header{Version: hb[2], Type: MsgType(hb[3]), Len: binary.LittleEndian.Uint16(hb[4:6]), Seq: binary.LittleEndian.Uint16(hb[6:8])}
	if int(h.Len) > MaxPayload {
		return h, nil, ErrTooLarge
	}
	p := buf[:h.Len]
	if _, err := io.ReadFull(r, p); err != nil {
		return h, nil, err
	}
	return h, p, nil
}

// Encode builds a full packet (header + payload).
func Encode(t MsgType, seq uint16, payload []byte) []byte {
	if len(payload) > MaxPayload {
		panic("protocol: payload too large")
	}
	out := make([]byte, HeaderSize+len(payload))
	binary.LittleEndian.PutUint16(out[0:2], Magic)
	out[2] = Version
	out[3] = byte(t)
	binary.LittleEndian.PutUint16(out[4:6], uint16(len(payload)))
	binary.LittleEndian.PutUint16(out[6:8], seq)
	copy(out[HeaderSize:], payload)
	return out
}

// ---------------------------------------------------------------- helpers

type writer struct{ b []byte }

func (w *writer) u8(v uint8)    { w.b = append(w.b, v) }
func (w *writer) u16(v uint16)  { w.b = binary.LittleEndian.AppendUint16(w.b, v) }
func (w *writer) u32(v uint32)  { w.b = binary.LittleEndian.AppendUint32(w.b, v) }
func (w *writer) i32(v int32)   { w.u32(uint32(v)) }
func (w *writer) f32(v float32) { w.u32(math.Float32bits(v)) }
func (w *writer) str(s string, n int) {
	b := make([]byte, n)
	copy(b, truncUTF8(s, n))
	w.b = append(w.b, b...)
}

type reader struct {
	b   []byte
	off int
	err error
}

func (r *reader) need(n int) bool {
	if r.err != nil {
		return false
	}
	if r.off+n > len(r.b) {
		r.err = ErrShort
		return false
	}
	return true
}
func (r *reader) u8() uint8 {
	if !r.need(1) {
		return 0
	}
	v := r.b[r.off]
	r.off++
	return v
}
func (r *reader) u16() uint16 {
	if !r.need(2) {
		return 0
	}
	v := binary.LittleEndian.Uint16(r.b[r.off:])
	r.off += 2
	return v
}
func (r *reader) u32() uint32 {
	if !r.need(4) {
		return 0
	}
	v := binary.LittleEndian.Uint32(r.b[r.off:])
	r.off += 4
	return v
}
func (r *reader) i32() int32 { return int32(r.u32()) }
func (r *reader) f32() float32 {
	v := math.Float32frombits(r.u32())
	if r.err == nil && !validFloat(v) {
		r.err = ErrBadFloat
	}
	return v
}
func (r *reader) str(n int) string {
	if !r.need(n) {
		return ""
	}
	s := r.b[r.off : r.off+n]
	r.off += n
	if i := indexZero(s); i >= 0 {
		s = s[:i]
	}
	return sanitize(string(s))
}
func (r *reader) rest() []byte {
	if r.err != nil {
		return nil
	}
	out := append([]byte(nil), r.b[r.off:]...)
	r.off = len(r.b)
	return out
}

// Positions in BDSP are small; anything huge is garbage or abuse.
func validFloat(v float32) bool {
	f := float64(v)
	return !math.IsNaN(f) && !math.IsInf(f, 0) && math.Abs(f) < 1e6
}

func indexZero(b []byte) int {
	for i, c := range b {
		if c == 0 {
			return i
		}
	}
	return -1
}

// sanitize keeps valid UTF-8 and drops control characters.
func sanitize(s string) string {
	s = strings.ToValidUTF8(s, "")
	return strings.Map(func(r rune) rune {
		if unicode.IsControl(r) {
			return -1
		}
		return r
	}, s)
}

// truncUTF8 cuts s to at most n bytes without splitting a rune.
func truncUTF8(s string, n int) string {
	if len(s) <= n {
		return s
	}
	s = s[:n]
	for len(s) > 0 && !utf8.ValidString(s) {
		s = s[:len(s)-1]
	}
	return s
}

// ---------------------------------------------------------------- messages

// Appearance is the visual metadata of a trainer (shared by HELLO_REQ and PLAYER_META).
type Appearance struct {
	Name          string // up to 24 bytes UTF-8
	FashionID     uint16
	ColorID       uint8
	Gender        uint8
	FollowSpecies uint16
	FollowForm    uint8
	FollowShiny   uint8
}

func (a *Appearance) write(w *writer) {
	w.str(a.Name, 24)
	w.u16(a.FashionID)
	w.u8(a.ColorID)
	w.u8(a.Gender)
	w.u16(a.FollowSpecies)
	w.u8(a.FollowForm)
	w.u8(a.FollowShiny)
}
func (a *Appearance) read(r *reader) {
	a.Name = r.str(24)
	a.FashionID = r.u16()
	a.ColorID = r.u8()
	a.Gender = r.u8()
	a.FollowSpecies = r.u16()
	a.FollowForm = r.u8()
	a.FollowShiny = r.u8()
}

// HelloReq (0x01, client → server). 52 bytes, or 68 with the optional password.
type HelloReq struct {
	RoomCode string // up to 16 bytes
	Appearance
	InitialZone int32
	Password    string // optional extension: 16 bytes appended after initial_zone_id
}

const HelloReqSize = 52
const HelloReqWithPasswordSize = 68

func (m *HelloReq) Marshal() []byte {
	w := &writer{}
	w.str(m.RoomCode, 16)
	m.Appearance.write(w)
	w.i32(m.InitialZone)
	if m.Password != "" {
		w.str(m.Password, 16)
	}
	return w.b
}
func (m *HelloReq) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.RoomCode = r.str(16)
	m.Appearance.read(r)
	m.InitialZone = r.i32()
	m.Password = ""
	if r.err == nil && len(b) >= HelloReqWithPasswordSize {
		m.Password = r.str(16)
	}
	return r.err
}

// HelloAck (0x02, server → client). 8 bytes.
type HelloAck struct {
	PlayerID     uint16
	TickRateHz   uint8
	Status       uint8
	ServerTimeMs uint32
}

func (m *HelloAck) Marshal() []byte {
	w := &writer{}
	w.u16(m.PlayerID)
	w.u8(m.TickRateHz)
	w.u8(m.Status)
	w.u32(m.ServerTimeMs)
	return w.b
}
func (m *HelloAck) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.PlayerID = r.u16()
	m.TickRateHz = r.u8()
	m.Status = r.u8()
	m.ServerTimeMs = r.u32()
	return r.err
}

// PlayerMeta (0x03, server → client). 38 bytes.
type PlayerMeta struct {
	PlayerID uint16
	Zone     int32
	Appearance
}

func (m *PlayerMeta) Marshal() []byte {
	w := &writer{}
	w.u16(m.PlayerID)
	w.i32(m.Zone)
	m.Appearance.write(w)
	return w.b
}
func (m *PlayerMeta) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.PlayerID = r.u16()
	m.Zone = r.i32()
	m.Appearance.read(r)
	return r.err
}

// ZoneChange (0x04, client → server). 24 bytes.
// Sending ZONE_CHANGE with new_zone_id equal to the current zone asks the server
// to resend PLAYER_META for every peer in that zone (used after battles/map loads).
type ZoneChange struct {
	OldZone, NewZone int32
	X, Y, Z, Yaw     float32
}

func (m *ZoneChange) Marshal() []byte {
	w := &writer{}
	w.i32(m.OldZone)
	w.i32(m.NewZone)
	w.f32(m.X)
	w.f32(m.Y)
	w.f32(m.Z)
	w.f32(m.Yaw)
	return w.b
}
func (m *ZoneChange) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.OldZone = r.i32()
	m.NewZone = r.i32()
	m.X, m.Y, m.Z, m.Yaw = r.f32(), r.f32(), r.f32(), r.f32()
	return r.err
}

// PlayerState (0x05, client → server, 20 Hz). 28 bytes. Also embedded in WORLD_SNAPSHOT.
type PlayerState struct {
	Zone                    int32
	X, Y, Z, Yaw, MoveSpeed float32
	AnimID                  uint16
	FormState               uint8 // 0 walk, 1 run, 2 bike, 3 surf
	Flags                   uint8 // FlagInBattle | FlagInMenu | FlagMegaActive
}

const PlayerStateSize = 28

func (m *PlayerState) write(w *writer) {
	w.i32(m.Zone)
	w.f32(m.X)
	w.f32(m.Y)
	w.f32(m.Z)
	w.f32(m.Yaw)
	w.f32(m.MoveSpeed)
	w.u16(m.AnimID)
	w.u8(m.FormState)
	w.u8(m.Flags)
}
func (m *PlayerState) read(r *reader) {
	m.Zone = r.i32()
	m.X, m.Y, m.Z, m.Yaw, m.MoveSpeed = r.f32(), r.f32(), r.f32(), r.f32(), r.f32()
	m.AnimID = r.u16()
	m.FormState = r.u8()
	m.Flags = r.u8()
}
func (m *PlayerState) Marshal() []byte { w := &writer{}; m.write(w); return w.b }
func (m *PlayerState) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.read(r)
	return r.err
}

// SnapshotEntry is one peer inside WORLD_SNAPSHOT (30 bytes).
type SnapshotEntry struct {
	PlayerID uint16
	State    PlayerState
}

// WorldSnapshot (0x06, server → client, 20 Hz). 9 + 30×count bytes.
type WorldSnapshot struct {
	ServerTimeMs uint32
	Zone         int32
	Entries      []SnapshotEntry
}

// MaxSnapshotEntries is how many peers fit in one 4096-byte payload.
const MaxSnapshotEntries = (MaxPayload - 9) / (2 + PlayerStateSize)

func (m *WorldSnapshot) Marshal() []byte {
	n := len(m.Entries)
	if n > MaxSnapshotEntries {
		n = MaxSnapshotEntries
	}
	w := &writer{b: make([]byte, 0, 9+30*n)}
	w.u32(m.ServerTimeMs)
	w.i32(m.Zone)
	w.u8(uint8(n))
	for i := 0; i < n; i++ {
		w.u16(m.Entries[i].PlayerID)
		m.Entries[i].State.write(w)
	}
	return w.b
}
func (m *WorldSnapshot) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.ServerTimeMs = r.u32()
	m.Zone = r.i32()
	n := int(r.u8())
	m.Entries = m.Entries[:0]
	for i := 0; i < n && r.err == nil; i++ {
		var e SnapshotEntry
		e.PlayerID = r.u16()
		e.State.read(r)
		m.Entries = append(m.Entries, e)
	}
	return r.err
}

// PlayerLeave (0x07, server → client). 3 bytes.
type PlayerLeave struct {
	PlayerID uint16
	Reason   uint8
}

func (m *PlayerLeave) Marshal() []byte { w := &writer{}; w.u16(m.PlayerID); w.u8(m.Reason); return w.b }
func (m *PlayerLeave) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.PlayerID = r.u16()
	m.Reason = r.u8()
	return r.err
}

// PingPong (0x08 / 0x09). 4 bytes. The server echoes a PING's timestamp in its PONG.
type PingPong struct{ TimestampMs uint32 }

func (m *PingPong) Marshal() []byte { w := &writer{}; w.u32(m.TimestampMs); return w.b }
func (m *PingPong) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.TimestampMs = r.u32()
	return r.err
}

// CoopEvent (0x10, both directions). 3 + len(data) bytes.
// Client → server: PlayerID is the target (BroadcastTarget = whole room).
// Server → client: PlayerID is the sender.
type CoopEvent struct {
	PlayerID  uint16
	EventType uint8
	Data      []byte
}

func (m *CoopEvent) Marshal() []byte {
	w := &writer{}
	w.u16(m.PlayerID)
	w.u8(m.EventType)
	w.b = append(w.b, m.Data...)
	return w.b
}
func (m *CoopEvent) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.PlayerID = r.u16()
	m.EventType = r.u8()
	m.Data = r.rest()
	return r.err
}
