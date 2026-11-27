// Package coop is the room / zone-partitioned relay server (guide §6.3, §6.6).
//
// All game state is owned by a single hub goroutine, so there are no locks:
// connection readers decode packets and hand them to the hub over a channel,
// and each connection has a writer goroutine fed by a buffered channel.
package coop

import (
	"context"
	"crypto/subtle"
	"errors"
	"io"
	"log"
	"net"
	"strings"
	"sync/atomic"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/store"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

type Config struct {
	MaxPlayersPerRoom int           // default 4
	MaxRooms          int           // default 16
	MaxConns          int           // total open TCP connections, default 64
	Password          string        // optional; if set, HELLO_REQ must carry it
	TickHz            int           // default 20
	Timeout           time.Duration // dead-client eviction, default 5s
	Logger            *log.Logger   // default log.Default()
	Verbose           bool

	DataPath       string        // JSON file for legendary owners + badges; "" = memory only
	Legendaries    []uint16      // species tracked by the registry; nil = DefaultLegendaries
	ReserveTimeout time.Duration // max time a legendary stays reserved during a battle, default 15m
	ReserveGrace   time.Duration // keep a reservation this long after its holder disconnects, default 60s
	TurnTimeout    time.Duration // battle turn deadline before missing actions are filled, default 90s
}

func (c *Config) defaults() {
	if c.MaxPlayersPerRoom <= 0 {
		c.MaxPlayersPerRoom = 4
	}
	if c.MaxRooms <= 0 {
		c.MaxRooms = 16
	}
	if c.MaxConns <= 0 {
		c.MaxConns = 64
	}
	if c.TickHz <= 0 {
		c.TickHz = 20
	}
	if c.Timeout <= 0 {
		c.Timeout = 5 * time.Second
	}
	if c.Logger == nil {
		c.Logger = log.Default()
	}
	if c.Legendaries == nil {
		c.Legendaries = DefaultLegendaries
	}
	if c.ReserveTimeout <= 0 {
		c.ReserveTimeout = 15 * time.Minute
	}
	if c.ReserveGrace <= 0 {
		c.ReserveGrace = 60 * time.Second
	}
	if c.TurnTimeout <= 0 {
		c.TurnTimeout = 90 * time.Second
	}
}

const (
	outQueue     = 256 // packets buffered per client before it's considered too slow
	writeTimeout = 5 * time.Second
	ratePerSec   = 60 // sustained packets/s allowed per client (20 Hz state + pings + events)
	rateBurst    = 120
	eventPerSec  = 10 // COOP_EVENTs/s per client
	eventBurst   = 20
)

type Server struct {
	cfg    Config
	events chan event
	start  time.Time
	conns  atomic.Int32

	// hub-owned state
	clients      map[*client]struct{}
	rooms        map[string]*room
	store        *store.Store
	legendSet    map[uint16]bool
	reservations map[legendKey]*reservation
	lobbies      map[uint16]*lobby
	nextLobby    uint16
}

type eventKind int

const (
	evOpen eventKind = iota
	evPacket
	evClosed
)

type event struct {
	kind    eventKind
	c       *client
	hdr     protocol.Header
	payload []byte
}

type bucket struct {
	tokens, rate, burst float64
	last                time.Time
}

func (b *bucket) take(now time.Time) bool {
	b.tokens += now.Sub(b.last).Seconds() * b.rate
	if b.tokens > b.burst {
		b.tokens = b.burst
	}
	b.last = now
	if b.tokens < 1 {
		return false
	}
	b.tokens--
	return true
}

type client struct {
	conn     net.Conn
	addr     string
	out      chan []byte
	gone     bool
	seq      uint16
	lastSeen time.Time
	pkts     bucket
	evts     bucket
	dropped  int

	room     *room
	key      string // stable identity: name + trainer/secret ID
	lobby    *lobby
	pid      uint16
	app      protocol.Appearance
	zone     int32
	state    protocol.PlayerState
	hasState bool
}

type room struct {
	code    string
	players map[uint16]*client
	zones   map[int32]map[uint16]*client
	nextID  uint16
}

func New(cfg Config) (*Server, error) {
	cfg.defaults()
	st, err := store.Open(cfg.DataPath)
	if err != nil {
		return nil, err
	}
	s := &Server{cfg: cfg, events: make(chan event, 1024), clients: map[*client]struct{}{}, rooms: map[string]*room{},
		store: st, legendSet: map[uint16]bool{}, reservations: map[legendKey]*reservation{}, lobbies: map[uint16]*lobby{}}
	for _, sp := range cfg.Legendaries {
		s.legendSet[sp] = true
	}
	return s, nil
}

func (s *Server) logf(format string, args ...any) { s.cfg.Logger.Printf(format, args...) }
func (s *Server) vlogf(format string, args ...any) {
	if s.cfg.Verbose {
		s.cfg.Logger.Printf(format, args...)
	}
}
func (s *Server) nowMs() uint32 { return uint32(time.Since(s.start).Milliseconds()) }

// Serve accepts connections on ln until ctx is cancelled.
func (s *Server) Serve(ctx context.Context, ln net.Listener) error {
	s.start = time.Now()
	go func() { <-ctx.Done(); ln.Close() }()
	hubDone := make(chan struct{})
	go func() { s.hub(ctx); close(hubDone) }()
	defer func() { <-hubDone }()
	for {
		conn, err := ln.Accept()
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			var ne net.Error
			if errors.As(err, &ne) && ne.Timeout() {
				time.Sleep(50 * time.Millisecond)
				continue
			}
			return err
		}
		if int(s.conns.Add(1)) > s.cfg.MaxConns {
			s.conns.Add(-1)
			conn.Close()
			continue
		}
		if tc, ok := conn.(*net.TCPConn); ok {
			tc.SetNoDelay(true)
			tc.SetKeepAlive(true)
			tc.SetKeepAlivePeriod(15 * time.Second)
		}
		now := time.Now()
		c := &client{
			conn: conn, addr: conn.RemoteAddr().String(), out: make(chan []byte, outQueue), lastSeen: now,
			pkts: bucket{tokens: rateBurst, rate: ratePerSec, burst: rateBurst, last: now},
			evts: bucket{tokens: eventBurst, rate: eventPerSec, burst: eventBurst, last: now},
		}
		go s.writeLoop(c)
		select {
		case s.events <- event{kind: evOpen, c: c}:
		case <-ctx.Done():
			conn.Close()
			return nil
		}
		go s.readLoop(ctx, c)
	}
}

func (s *Server) readLoop(ctx context.Context, c *client) {
	defer s.conns.Add(-1)
	buf := make([]byte, protocol.MaxPayload)
	for {
		hdr, p, err := protocol.ReadPacket(c.conn, buf)
		if err != nil {
			if !errors.Is(err, io.EOF) && !errors.Is(err, net.ErrClosed) {
				s.vlogf("[%s] read error: %v", c.addr, err)
			}
			c.conn.Close()
			select {
			case s.events <- event{kind: evClosed, c: c}:
			case <-ctx.Done():
			}
			return
		}
		ev := event{kind: evPacket, c: c, hdr: hdr, payload: append([]byte(nil), p...)}
		select {
		case s.events <- ev:
		case <-ctx.Done():
			return
		}
	}
}

func (s *Server) writeLoop(c *client) {
	for pkt := range c.out {
		c.conn.SetWriteDeadline(time.Now().Add(writeTimeout))
		if _, err := c.conn.Write(pkt); err != nil {
			c.conn.Close()
			for range c.out { // drain until the hub closes the channel
			}
			return
		}
	}
	c.conn.Close()
}

// ---------------------------------------------------------------- hub

func (s *Server) hub(ctx context.Context) {
	tick := time.NewTicker(time.Second / time.Duration(s.cfg.TickHz))
	reap := time.NewTicker(500 * time.Millisecond)
	defer tick.Stop()
	defer reap.Stop()
	for {
		select {
		case <-ctx.Done():
			for c := range s.clients {
				s.remove(c, protocol.LeaveDisconnect, "server shutdown")
			}
			return
		case ev := <-s.events:
			switch ev.kind {
			case evOpen:
				s.clients[ev.c] = struct{}{}
				s.vlogf("[%s] connected", ev.c.addr)
			case evClosed:
				s.remove(ev.c, protocol.LeaveDisconnect, "disconnected")
			case evPacket:
				if !ev.c.gone {
					s.handle(ev.c, ev.hdr, ev.payload)
				}
			}
		case <-tick.C:
			s.broadcastSnapshots()
		case now := <-reap.C:
			for c := range s.clients {
				if now.Sub(c.lastSeen) > s.cfg.Timeout {
					s.remove(c, protocol.LeaveTimeout, "timed out")
				}
			}
			s.featureTick(now)
		}
	}
}

func (s *Server) send(c *client, t protocol.MsgType, payload []byte) {
	if c.gone {
		return
	}
	c.seq++
	select {
	case c.out <- protocol.Encode(t, c.seq, payload):
	default:
		s.remove(c, protocol.LeaveDisconnect, "too slow (send queue full)")
		c.conn.Close()
	}
}

// remove is idempotent: detaches c from its room, tells peers, and closes it.
func (s *Server) remove(c *client, reason uint8, why string) {
	if c.gone {
		return
	}
	c.gone = true
	delete(s.clients, c)
	if r := c.room; r != nil {
		s.lobbyLeave(c)
		s.reservationGrace(c)
		s.leaveZone(c)
		delete(r.players, c.pid)
		leave := (&protocol.PlayerLeave{PlayerID: c.pid, Reason: reason}).Marshal()
		for _, p := range r.players {
			s.send(p, protocol.MsgPlayerLeave, leave)
		}
		s.logf("room %s: %q (#%d) left: %s (%d/%d)", r.code, c.app.Name, c.pid, why, len(r.players), s.cfg.MaxPlayersPerRoom)
		if len(r.players) == 0 {
			delete(s.rooms, r.code)
			s.logf("room %s closed (empty)", r.code)
		}
		c.room = nil
	} else {
		s.vlogf("[%s] %s", c.addr, why)
	}
	close(c.out)
}

func (s *Server) handle(c *client, h protocol.Header, p []byte) {
	now := time.Now()
	if h.Version != protocol.Version {
		if h.Type == protocol.MsgHelloReq {
			s.send(c, protocol.MsgHelloAck, (&protocol.HelloAck{Status: protocol.StatusBadVersion}).Marshal())
		}
		s.remove(c, protocol.LeaveDisconnect, "protocol version mismatch")
		return
	}
	if !c.pkts.take(now) {
		c.dropped++
		if c.dropped > 2*rateBurst {
			s.remove(c, protocol.LeaveDisconnect, "flooding")
		}
		return
	}
	c.lastSeen = now

	switch h.Type {
	case protocol.MsgPing:
		var m protocol.PingPong
		if m.Unmarshal(p) == nil {
			s.send(c, protocol.MsgPong, m.Marshal())
		}
	case protocol.MsgPong:
		// keep-alive only
	case protocol.MsgHelloReq:
		s.onHello(c, p)
	default:
		if c.room == nil {
			s.remove(c, protocol.LeaveDisconnect, "sent "+h.Type.String()+" before HELLO")
			return
		}
		switch h.Type {
		case protocol.MsgZoneChange:
			var m protocol.ZoneChange
			if err := m.Unmarshal(p); err != nil {
				s.vlogf("[%s] bad ZONE_CHANGE: %v", c.addr, err)
				return
			}
			c.state.X, c.state.Y, c.state.Z, c.state.Yaw = m.X, m.Y, m.Z, m.Yaw
			c.state.Zone = m.NewZone
			c.hasState = true
			if m.NewZone == c.zone {
				s.sendZoneRoster(c) // resync request
			} else {
				s.moveZone(c, m.NewZone)
			}
		case protocol.MsgPlayerState:
			var m protocol.PlayerState
			if err := m.Unmarshal(p); err != nil {
				s.vlogf("[%s] bad PLAYER_STATE: %v", c.addr, err)
				return
			}
			if m.Zone != c.zone { // zone desync guard: implicit ZONE_CHANGE
				s.moveZone(c, m.Zone)
			}
			c.state = m
			c.hasState = true
		case protocol.MsgCoopEvent:
			s.onCoopEvent(c, p, now)
		default:
			if s.handleFeature(c, h.Type, p, now) {
				return
			}
			s.vlogf("[%s] ignoring %s", c.addr, h.Type)
		}
	}
}

// NormalizeRoomCode upper-cases and validates a room code (A-Z, 0-9, '-', '_').
func NormalizeRoomCode(code string) (string, bool) {
	code = strings.ToUpper(strings.TrimSpace(code))
	if code == "" {
		code = "DEFAULT"
	}
	for _, r := range code {
		if !(r >= 'A' && r <= 'Z' || r >= '0' && r <= '9' || r == '-' || r == '_') {
			return "", false
		}
	}
	return code, true
}

func (s *Server) onHello(c *client, p []byte) {
	ack := func(status uint8) {
		s.send(c, protocol.MsgHelloAck, (&protocol.HelloAck{TickRateHz: uint8(s.cfg.TickHz), Status: status, ServerTimeMs: s.nowMs()}).Marshal())
	}
	if c.room != nil {
		ack(protocol.StatusAlreadyInRoom)
		return
	}
	var m protocol.HelloReq
	if err := m.Unmarshal(p); err != nil {
		s.remove(c, protocol.LeaveDisconnect, "bad HELLO_REQ")
		return
	}
	fail := func(status uint8, why string) {
		ack(status)
		s.remove(c, protocol.LeaveDisconnect, why)
	}
	if s.cfg.Password != "" && subtle.ConstantTimeCompare([]byte(m.Password), []byte(s.cfg.Password)) != 1 {
		fail(protocol.StatusBadPassword, "wrong password")
		return
	}
	code, ok := NormalizeRoomCode(m.RoomCode)
	if !ok {
		fail(protocol.StatusBadRoomCode, "invalid room code")
		return
	}
	r := s.rooms[code]
	if r == nil {
		if len(s.rooms) >= s.cfg.MaxRooms {
			fail(protocol.StatusServerFull, "server full (max rooms)")
			return
		}
		r = &room{code: code, players: map[uint16]*client{}, zones: map[int32]map[uint16]*client{}}
		s.rooms[code] = r
		s.logf("room %s opened", code)
	}
	if len(r.players) >= s.cfg.MaxPlayersPerRoom {
		fail(protocol.StatusRoomFull, "room "+code+" full")
		return
	}
	for {
		r.nextID++
		if r.nextID != 0 && r.nextID != protocol.BroadcastTarget && r.players[r.nextID] == nil {
			break
		}
	}
	if strings.TrimSpace(m.Name) == "" {
		m.Name = "Trainer"
	}
	c.room, c.pid, c.app = r, r.nextID, m.Appearance
	c.key = identityKey(m.Name, m.TrainerID, m.SecretID)
	r.players[c.pid] = c
	s.send(c, protocol.MsgHelloAck, (&protocol.HelloAck{PlayerID: c.pid, TickRateHz: uint8(s.cfg.TickHz), Status: protocol.StatusOK, ServerTimeMs: s.nowMs()}).Marshal())
	s.logf("room %s: %q (#%d) joined from %s, zone %d (%d/%d)", code, m.Name, c.pid, c.addr, m.InitialZone, len(r.players), s.cfg.MaxPlayersPerRoom)
	c.zone = m.InitialZone
	c.state.Zone = m.InitialZone
	s.enterZone(c)
	s.sendInitialFeatureState(c)
}

func (s *Server) meta(c *client) []byte {
	return (&protocol.PlayerMeta{PlayerID: c.pid, Zone: c.zone, Appearance: c.app}).Marshal()
}

// sendZoneRoster sends c the PLAYER_META of every peer in c's zone.
func (s *Server) sendZoneRoster(c *client) {
	for id, p := range c.room.zones[c.zone] {
		if id != c.pid {
			s.send(c, protocol.MsgPlayerMeta, s.meta(p))
		}
	}
}

func (s *Server) enterZone(c *client) {
	r := c.room
	z := r.zones[c.zone]
	if z == nil {
		z = map[uint16]*client{}
		r.zones[c.zone] = z
	}
	s.sendZoneRoster(c)
	m := s.meta(c)
	for _, p := range z {
		s.send(p, protocol.MsgPlayerMeta, m)
	}
	z[c.pid] = c
}

func (s *Server) leaveZone(c *client) {
	r := c.room
	z := r.zones[c.zone]
	if z == nil {
		return
	}
	delete(z, c.pid)
	if len(z) == 0 {
		delete(r.zones, c.zone)
	}
}

func (s *Server) moveZone(c *client, newZone int32) {
	r := c.room
	s.leaveZone(c)
	leave := (&protocol.PlayerLeave{PlayerID: c.pid, Reason: protocol.LeaveZoneExit}).Marshal()
	for _, p := range r.zones[c.zone] {
		s.send(p, protocol.MsgPlayerLeave, leave)
	}
	s.vlogf("room %s: #%d zone %d -> %d", r.code, c.pid, c.zone, newZone)
	c.zone = newZone
	s.enterZone(c)
}

func (s *Server) onCoopEvent(c *client, p []byte, now time.Time) {
	if !c.evts.take(now) {
		return
	}
	var m protocol.CoopEvent
	if err := m.Unmarshal(p); err != nil {
		return
	}
	target := m.PlayerID
	m.PlayerID = c.pid // server → client carries the sender
	out := m.Marshal()
	if target == protocol.BroadcastTarget {
		for id, peer := range c.room.players {
			if id != c.pid {
				s.send(peer, protocol.MsgCoopEvent, out)
			}
		}
		return
	}
	if peer := c.room.players[target]; peer != nil && target != c.pid {
		s.send(peer, protocol.MsgCoopEvent, out)
	}
}

func (s *Server) broadcastSnapshots() {
	now := s.nowMs()
	for _, r := range s.rooms {
		for zid, z := range r.zones {
			if len(z) < 2 {
				continue
			}
			for rid, recv := range z {
				snap := protocol.WorldSnapshot{ServerTimeMs: now, Zone: zid}
				for id, p := range z {
					if id != rid && p.hasState {
						snap.Entries = append(snap.Entries, protocol.SnapshotEntry{PlayerID: id, State: p.state})
					}
				}
				if len(snap.Entries) > 0 {
					s.send(recv, protocol.MsgWorldSnapshot, snap.Marshal())
				}
			}
		}
	}
}
