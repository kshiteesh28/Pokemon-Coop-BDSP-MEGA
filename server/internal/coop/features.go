package coop

import (
	"crypto/rand"
	"encoding/binary"
	"fmt"
	"sort"
	"strings"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/store"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

// DefaultLegendaries is the National Dex numbers of legendary and mythical
// species obtainable in BDSP / Luminescent Platinum. Override with -legendaries.
var DefaultLegendaries = []uint16{
	144, 145, 146, 150, 151, // Kanto birds, Mewtwo, Mew
	243, 244, 245, 249, 250, 251, // Johto beasts, Lugia, Ho-Oh, Celebi
	377, 378, 379, 380, 381, 382, 383, 384, 385, 386, // Hoenn
	480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, // Sinnoh
}

// identityKey gives a player a stable ID across sessions. Without a trainer ID
// it falls back to the name alone, which is weaker (two people could share a name).
func identityKey(name string, tid, sid uint32) string {
	name = strings.TrimSpace(name)
	if tid == 0 && sid == 0 {
		return name
	}
	return fmt.Sprintf("%s#%d-%d", name, tid, sid)
}

// ---------------------------------------------------------------- legendary registry (per room)

type legendKey struct {
	room    string
	species uint16
}

type reservation struct {
	key     string // holder identity
	name    string
	expires time.Time
}

func (s *Server) handleFeature(c *client, t protocol.MsgType, p []byte, now time.Time) bool {
	switch t {
	case protocol.MsgLegendReserve:
		var m protocol.LegendReserve
		if m.Unmarshal(p) == nil {
			s.legendReserve(c, m.Species, now)
		}
	case protocol.MsgLegendResult:
		var m protocol.LegendResult
		if m.Unmarshal(p) == nil {
			s.legendResult(c, m)
		}
	case protocol.MsgLegendTransfer:
		var m protocol.LegendTransfer
		if m.Unmarshal(p) == nil {
			s.legendTransfer(c, m)
		}
	case protocol.MsgBattleCreate:
		var m protocol.BattleCreate
		if m.Unmarshal(p) == nil {
			s.battleCreate(c, m)
		}
	case protocol.MsgBattleInvite:
		var m protocol.BattleInvite
		if m.Unmarshal(p) == nil {
			s.battleInvite(c, m)
		}
	case protocol.MsgBattleJoin:
		var m protocol.LobbyRef
		if m.Unmarshal(p) == nil {
			s.battleJoin(c, m.LobbyID)
		}
	case protocol.MsgBattleLeave:
		var m protocol.LobbyRef
		if m.Unmarshal(p) == nil && c.lobby != nil && c.lobby.id == m.LobbyID {
			s.lobbyLeave(c)
		}
	case protocol.MsgBattleReady:
		var m protocol.BattleReady
		if m.Unmarshal(p) == nil {
			s.battleReady(c, m)
		}
	case protocol.MsgBattleStart:
		var m protocol.LobbyRef
		if m.Unmarshal(p) == nil {
			s.battleStart(c, m.LobbyID, now)
		}
	case protocol.MsgBattleAction:
		var m protocol.BattleAction
		if m.Unmarshal(p) == nil {
			s.battleAction(c, m, now)
		}
	case protocol.MsgChat:
		s.onChat(c, p, now)
	case protocol.MsgBattleReport:
		var m protocol.BattleReport
		if m.Unmarshal(p) == nil {
			s.battleReport(c, m)
		}
	case protocol.MsgBattleResult:
		var m protocol.BattleResult
		if m.Unmarshal(p) == nil {
			s.battleResult(c, m)
		}
	default:
		return false
	}
	return true
}

func (s *Server) sendInitialFeatureState(c *client) {
	room := c.room.code
	for sp, l := range s.store.Legends(room) {
		s.send(c, protocol.MsgLegendUpdate, (&protocol.LegendUpdate{Species: sp, State: protocol.LegendOwned, OwnerName: l.OwnerName}).Marshal())
	}
	for k, r := range s.reservations {
		if k.room == room {
			s.send(c, protocol.MsgLegendUpdate, (&protocol.LegendUpdate{Species: k.species, State: protocol.LegendReserved, OwnerName: r.name}).Marshal())
		}
	}
	s.sendProgress(c)
	// Joining from the game while your app is already in a lobby: tell the game.
	if m := s.lobbyMember(c); m != c && m.lobby != nil {
		s.send(c, protocol.MsgBattleLobby, s.lobbyState(m.lobby))
	}
}

func (s *Server) broadcastLegend(room string, species uint16, state uint8, owner string) {
	r := s.rooms[room]
	if r == nil {
		return
	}
	msg := (&protocol.LegendUpdate{Species: species, State: state, OwnerName: owner}).Marshal()
	for _, p := range r.players {
		s.send(p, protocol.MsgLegendUpdate, msg)
	}
}

func (s *Server) legendReserve(c *client, species uint16, now time.Time) {
	ack := func(status uint8, holder string) {
		s.send(c, protocol.MsgLegendReserveAck, (&protocol.LegendReserveAck{Species: species, Status: status, HolderName: holder}).Marshal())
	}
	if !s.legendSet[species] {
		ack(protocol.LegendUntracked, "")
		return
	}
	room := c.room.code
	if l, ok := s.store.Legend(room, species); ok {
		if l.OwnerKey == c.key {
			ack(protocol.LegendYouOwn, l.OwnerName)
		} else {
			ack(protocol.LegendOwnedByOther, l.OwnerName)
		}
		return
	}
	k := legendKey{room, species}
	if r := s.reservations[k]; r != nil {
		if r.key != c.key {
			ack(protocol.LegendReservedOther, r.name)
			return
		}
		r.expires = now.Add(s.cfg.ReserveTimeout) // same player re-encountering: refresh
		ack(protocol.LegendGranted, r.name)
		return
	}
	s.reservations[k] = &reservation{key: c.key, name: c.app.Name, expires: now.Add(s.cfg.ReserveTimeout)}
	ack(protocol.LegendGranted, c.app.Name)
	s.broadcastLegend(room, species, protocol.LegendReserved, c.app.Name)
	s.logf("room %s: legendary %d reserved by %q", room, species, c.app.Name)
}

func (s *Server) legendResult(c *client, m protocol.LegendResult) {
	room := c.room.code
	k := legendKey{room, m.Species}
	switch m.Result {
	case protocol.LegendCaught, protocol.LegendEscaped:
		r := s.reservations[k]
		if r == nil || r.key != c.key {
			return // must hold the reservation
		}
		delete(s.reservations, k)
		if m.Result == protocol.LegendEscaped {
			s.broadcastLegend(room, m.Species, protocol.LegendFree, "")
			s.logf("room %s: legendary %d escaped from %q (available again)", room, m.Species, c.app.Name)
			return
		}
		if err := s.store.SetLegend(room, m.Species, store.Legend{OwnerKey: c.key, OwnerName: c.app.Name, Since: time.Now().UTC()}); err != nil {
			s.logf("ERROR saving data: %v", err)
		}
		s.broadcastLegend(room, m.Species, protocol.LegendOwned, c.app.Name)
		s.logf("room %s: legendary %d CAUGHT by %q", room, m.Species, c.app.Name)
	case protocol.LegendReleased:
		if l, ok := s.store.Legend(room, m.Species); ok && l.OwnerKey == c.key {
			if err := s.store.DeleteLegend(room, m.Species); err != nil {
				s.logf("ERROR saving data: %v", err)
			}
			s.broadcastLegend(room, m.Species, protocol.LegendFree, "")
			s.logf("room %s: legendary %d released by %q", room, m.Species, c.app.Name)
		}
	}
}

func (s *Server) legendTransfer(c *client, m protocol.LegendTransfer) {
	room := c.room.code
	l, ok := s.store.Legend(room, m.Species)
	to := c.room.players[m.ToPlayer]
	if !ok || l.OwnerKey != c.key || to == nil || to == c {
		return
	}
	if err := s.store.SetLegend(room, m.Species, store.Legend{OwnerKey: to.key, OwnerName: to.app.Name, Since: time.Now().UTC()}); err != nil {
		s.logf("ERROR saving data: %v", err)
	}
	s.broadcastLegend(room, m.Species, protocol.LegendOwned, to.app.Name)
	s.logf("room %s: legendary %d traded %q -> %q", room, m.Species, c.app.Name, to.app.Name)
}

// reservationGrace shortens a disconnecting player's reservations so a crash
// doesn't lock a legendary for 15 minutes; reconnecting in time keeps it.
func (s *Server) reservationGrace(c *client) {
	deadline := time.Now().Add(s.cfg.ReserveGrace)
	for k, r := range s.reservations {
		if k.room == c.room.code && r.key == c.key && r.expires.After(deadline) {
			r.expires = deadline
		}
	}
}

// ---------------------------------------------------------------- battle lobbies

// maxPlayersFor caps lobby size by battle kind: gyms and the Elite Four are solo or duo only.
func maxPlayersFor(kind uint8) uint8 {
	if kind == protocol.BattleGym || kind == protocol.BattleEliteFour {
		return protocol.MaxDuoPlayers
	}
	return protocol.MaxBattlePlayers
}

type lobby struct {
	id       uint16
	room     *room
	kind     uint8
	target   uint16
	max      int
	host     *client
	members  []*client
	ready    map[*client]bool
	state    uint8
	turn     uint16
	actions  map[*client][]byte
	deadline time.Time
	reports  map[*client]uint8 // in-game results during a team challenge
}

func (s *Server) battleErr(c *client, id uint16, code uint8) {
	s.send(c, protocol.MsgBattleError, (&protocol.BattleError{LobbyID: id, Code: code}).Marshal())
}

func (s *Server) lobbyState(l *lobby) []byte {
	m := protocol.BattleLobby{LobbyID: l.id, Kind: l.kind, Target: l.target, Host: l.host.pid, State: l.state, MaxPlayers: uint8(l.max)}
	for _, c := range l.members {
		bm := protocol.BattleMember{PlayerID: c.pid}
		if l.ready[c] {
			bm.Ready = 1
		}
		if l.kind <= protocol.BattleChampion && s.store.HasAchievement(c.key, store.Achievement{Kind: l.kind, Target: l.target}) {
			bm.Completed = 1
		}
		bm.Report = l.reports[c]
		m.Members = append(m.Members, bm)
	}
	return m.Marshal()
}

// broadcastLobby sends to every member, and also to their linked
// connections: the same player connected from both the app and the game.
func (s *Server) broadcastLobby(l *lobby, t protocol.MsgType, payload []byte) {
	for _, c := range l.members {
		s.send(c, t, payload)
		for _, other := range l.room.players {
			if other != c && other.lobby == nil && other.key == c.key {
				s.send(other, t, payload)
			}
		}
	}
}

// lobbyMember returns the connection that's actually in a lobby for this
// player: c itself, or the same player's other connection in the room (e.g.
// the lobby was made in the app, and c is the game).
func (s *Server) lobbyMember(c *client) *client {
	if c.lobby != nil || c.room == nil {
		return c
	}
	for _, other := range c.room.players {
		if other != c && other.lobby != nil && other.key == c.key {
			return other
		}
	}
	return c
}

func (s *Server) battleCreate(c *client, m protocol.BattleCreate) {
	if c.lobby != nil {
		s.battleErr(c, c.lobby.id, protocol.BattleErrInLobby)
		return
	}
	if m.Kind > protocol.BattleFree || m.MaxPlayers < 1 || m.MaxPlayers > maxPlayersFor(m.Kind) {
		s.battleErr(c, 0, protocol.BattleErrBadRequest)
		return
	}
	for {
		s.nextLobby++
		if s.nextLobby != 0 && s.lobbies[s.nextLobby] == nil {
			break
		}
	}
	l := &lobby{id: s.nextLobby, room: c.room, kind: m.Kind, target: m.Target, max: int(m.MaxPlayers), host: c,
		members: []*client{c}, ready: map[*client]bool{}, actions: map[*client][]byte{}, reports: map[*client]uint8{}}
	s.lobbies[l.id] = l
	c.lobby = l
	s.send(c, protocol.MsgBattleLobby, s.lobbyState(l))
	s.logf("room %s: %q opened battle lobby %d (kind %d, target %d, up to %d players)", c.room.code, c.app.Name, l.id, l.kind, l.target, l.max)
}

func (s *Server) battleInvite(c *client, m protocol.BattleInvite) {
	l := c.lobby
	if l == nil || l.id != m.LobbyID {
		s.battleErr(c, m.LobbyID, protocol.BattleErrNotMember)
		return
	}
	to := c.room.players[m.PlayerID]
	if to == nil || to == c {
		s.battleErr(c, m.LobbyID, protocol.BattleErrBadRequest)
		return
	}
	s.send(to, protocol.MsgBattleInvite, (&protocol.BattleInvite{LobbyID: l.id, PlayerID: c.pid, Kind: l.kind, Target: l.target}).Marshal())
}

func (s *Server) battleJoin(c *client, id uint16) {
	l := s.lobbies[id]
	switch {
	case l == nil:
		s.battleErr(c, id, protocol.BattleErrNoLobby)
	case l.room != c.room:
		s.battleErr(c, id, protocol.BattleErrOtherRoom)
	case c.lobby != nil:
		s.battleErr(c, id, protocol.BattleErrInLobby)
	case l.state != protocol.LobbyOpen:
		s.battleErr(c, id, protocol.BattleErrStarted)
	case len(l.members) >= l.max:
		s.battleErr(c, id, protocol.BattleErrFull)
	default:
		l.members = append(l.members, c)
		c.lobby = l
		s.broadcastLobby(l, protocol.MsgBattleLobby, s.lobbyState(l))
	}
}

// lobbyLeave removes c from its lobby (leave, disconnect or timeout). Mid-battle,
// the remaining players continue; their games treat the slot as AI-controlled.
func (s *Server) lobbyLeave(c *client) {
	l := c.lobby
	if l == nil {
		return
	}
	c.lobby = nil
	for i, m := range l.members {
		if m == c {
			l.members = append(l.members[:i], l.members[i+1:]...)
			break
		}
	}
	delete(l.ready, c)
	delete(l.actions, c)
	delete(l.reports, c)
	if len(l.members) == 0 {
		delete(s.lobbies, l.id)
		return
	}
	if l.host == c {
		l.host = l.members[0]
	}
	s.broadcastLobby(l, protocol.MsgBattleLobby, s.lobbyState(l))
	if l.state == protocol.LobbyInBattle {
		s.maybeFinishTurn(l, false)
		s.maybeFinishFromReports(l)
	}
}

func (s *Server) battleReady(c *client, m protocol.BattleReady) {
	l := c.lobby
	if l == nil || l.id != m.LobbyID {
		s.battleErr(c, m.LobbyID, protocol.BattleErrNotMember)
		return
	}
	if l.state != protocol.LobbyOpen {
		return
	}
	l.ready[c] = m.Ready != 0
	s.broadcastLobby(l, protocol.MsgBattleLobby, s.lobbyState(l))
}

func (s *Server) battleStart(c *client, id uint16, now time.Time) {
	l := c.lobby
	switch {
	case l == nil || l.id != id:
		s.battleErr(c, id, protocol.BattleErrNotMember)
		return
	case l.host != c:
		s.battleErr(c, id, protocol.BattleErrNotHost)
		return
	case l.state != protocol.LobbyOpen:
		s.battleErr(c, id, protocol.BattleErrStarted)
		return
	}
	for _, m := range l.members {
		if !l.ready[m] {
			s.battleErr(c, id, protocol.BattleErrNotReady)
			return
		}
	}
	var seed [8]byte
	rand.Read(seed[:])
	start := protocol.BattleStart{LobbyID: l.id, Seed: binary.LittleEndian.Uint64(seed[:])}
	for _, m := range l.members {
		start.Order = append(start.Order, m.pid)
	}
	l.state, l.turn, l.deadline = protocol.LobbyInBattle, 1, now.Add(s.cfg.TurnTimeout)
	s.broadcastLobby(l, protocol.MsgBattleStart, start.Marshal())
	s.broadcastLobby(l, protocol.MsgBattleLobby, s.lobbyState(l))
	s.logf("room %s: battle lobby %d started with %d players", l.room.code, l.id, len(l.members))
}

func (s *Server) battleAction(c *client, m protocol.BattleAction, now time.Time) {
	l := c.lobby
	if l == nil || l.id != m.LobbyID || l.state != protocol.LobbyInBattle {
		s.battleErr(c, m.LobbyID, protocol.BattleErrNotMember)
		return
	}
	if m.Turn != l.turn || len(m.Data) > protocol.MaxBattleActionData {
		s.battleErr(c, m.LobbyID, protocol.BattleErrBadTurn)
		return
	}
	l.actions[c] = m.Data
	s.maybeFinishTurn(l, false)
}

// maybeFinishTurn broadcasts the turn once every member has acted, or, if
// force is set (deadline passed), with empty actions for whoever hasn't.
func (s *Server) maybeFinishTurn(l *lobby, force bool) {
	if !force && len(l.actions) < len(l.members) {
		return
	}
	t := protocol.BattleTurn{LobbyID: l.id, Turn: l.turn}
	for _, m := range l.members {
		t.Actions = append(t.Actions, protocol.TurnAction{PlayerID: m.pid, Data: l.actions[m]})
	}
	sort.Slice(t.Actions, func(i, j int) bool { return t.Actions[i].PlayerID < t.Actions[j].PlayerID })
	s.broadcastLobby(l, protocol.MsgBattleTurn, t.Marshal())
	l.turn++
	l.actions = map[*client][]byte{}
	l.deadline = time.Now().Add(s.cfg.TurnTimeout)
}

func (s *Server) battleResult(c *client, m protocol.BattleResult) {
	l := c.lobby
	if l == nil || l.id != m.LobbyID || l.state != protocol.LobbyInBattle {
		s.battleErr(c, m.LobbyID, protocol.BattleErrNotMember)
		return
	}
	if l.host != c {
		s.battleErr(c, m.LobbyID, protocol.BattleErrNotHost)
		return
	}
	s.finishBattle(l, m.Outcome, "reported by host")
}

// battleReport records one member's in-game result; when everyone has
// reported, the team wins only if all of them won.
func (s *Server) battleReport(c *client, m protocol.BattleReport) {
	c = s.lobbyMember(c) // the game reports for the lobby its player joined in the app
	l := c.lobby
	if l == nil || l.id != m.LobbyID || l.state != protocol.LobbyInBattle {
		s.battleErr(c, m.LobbyID, protocol.BattleErrNotMember)
		return
	}
	r := protocol.ReportLost
	if m.Outcome == protocol.OutcomeWin {
		r = protocol.ReportWon
	}
	l.reports[c] = r
	s.logf("room %s: %q %s their battle in lobby %d (trainer %d)", l.room.code, c.app.Name, map[bool]string{true: "won", false: "lost"}[r == protocol.ReportWon], l.id, m.TrainerID)
	s.broadcastLobby(l, protocol.MsgBattleLobby, s.lobbyState(l))
	s.maybeFinishFromReports(l)
}

func (s *Server) maybeFinishFromReports(l *lobby) {
	if l.state != protocol.LobbyInBattle || len(l.members) == 0 {
		return
	}
	outcome := protocol.OutcomeWin
	for _, m := range l.members {
		switch l.reports[m] {
		case protocol.ReportPending:
			return // someone is still fighting
		case protocol.ReportLost:
			outcome = protocol.OutcomeLoss
		}
	}
	s.finishBattle(l, outcome, "everyone reported from their game")
}

func (s *Server) finishBattle(l *lobby, outcome uint8, how string) {
	end := protocol.BattleEnd{LobbyID: l.id, Outcome: outcome}
	for _, p := range l.members {
		end.Players = append(end.Players, p.pid)
	}
	credit := outcome == protocol.OutcomeWin && l.kind <= protocol.BattleChampion
	s.broadcastLobby(l, protocol.MsgBattleEnd, end.Marshal())
	for _, p := range append([]*client(nil), l.members...) {
		if credit {
			if added, err := s.store.AddAchievement(p.key, store.Achievement{Kind: l.kind, Target: l.target}); err != nil {
				s.logf("ERROR saving data: %v", err)
			} else if added {
				s.sendProgress(p)
			}
		}
		p.lobby = nil
	}
	delete(s.lobbies, l.id)
	result := map[bool]string{true: "WON", false: "lost"}[outcome == protocol.OutcomeWin]
	s.logf("room %s: battle lobby %d %s (kind %d, target %d, %d players, %s)", l.room.code, l.id, result, l.kind, l.target, len(end.Players), how)
}

func (s *Server) sendProgress(c *client) {
	var m protocol.Progress
	for _, a := range s.store.Progress(c.key) {
		m.Entries = append(m.Entries, protocol.ProgressEntry{Kind: a.Kind, Target: a.Target})
	}
	s.send(c, protocol.MsgProgress, m.Marshal())
}

// featureTick runs twice a second: expires reservations and forces stalled turns.
func (s *Server) featureTick(now time.Time) {
	for k, r := range s.reservations {
		if now.After(r.expires) {
			delete(s.reservations, k)
			s.broadcastLegend(k.room, k.species, protocol.LegendFree, "")
			s.logf("room %s: reservation on legendary %d by %q expired", k.room, k.species, r.name)
		}
	}
	for _, l := range s.lobbies {
		if l.state == protocol.LobbyInBattle && now.After(l.deadline) {
			s.maybeFinishTurn(l, true)
		}
	}
}

// ---------------------------------------------------------------- room chat

func (s *Server) onChat(c *client, p []byte, now time.Time) {
	var m protocol.ChatSend
	m.Unmarshal(p)
	text := strings.TrimSpace(m.Text)
	if text == "" {
		return
	}
	out := (&protocol.ChatMessage{PlayerID: c.pid, Name: c.app.Name, Text: text}).Marshal()
	for _, peer := range c.room.players {
		s.send(peer, protocol.MsgChat, out)
	}
	s.vlogf("room %s chat <%s> %s", c.room.code, c.app.Name, text)
}
