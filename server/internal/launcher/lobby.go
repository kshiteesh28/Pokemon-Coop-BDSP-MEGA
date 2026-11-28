package launcher

import (
	"fmt"
	"sort"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

// Battle lobbies from the app: team up for a gym, Elite Four member or the
// Champion. The server keeps the lobby; until the in-game battle sync exists,
// the host reports the result here and the whole team's badges are recorded.

type RosterView struct {
	ID   uint16 `json:"id"`
	Name string `json:"name"`
	Zone int32  `json:"zone"`
	You  bool   `json:"you"`
}

type LobbyMemberView struct {
	ID     uint16 `json:"id"`
	Name   string `json:"name"`
	Ready  bool   `json:"ready"`
	Helper bool   `json:"helper"` // already beat this one: joining to help
	You    bool   `json:"you"`
	IsHost bool   `json:"host"`
	Result string `json:"result"` // during the battle: "", "won" or "lost" (reported by their game)
}

type LobbyView struct {
	ID         uint16            `json:"id"`
	Kind       uint8             `json:"kind"`
	Target     uint16            `json:"target"`
	MaxPlayers uint8             `json:"max"`
	InBattle   bool              `json:"in_battle"`
	YouAreHost bool              `json:"you_are_host"`
	Members    []LobbyMemberView `json:"members"`
}

type InviteView struct {
	LobbyID uint16 `json:"lobby"`
	From    string `json:"from"`
	Kind    uint8  `json:"kind"`
	Target  uint16 `json:"target"`
}

type BattleNote struct {
	Seq  int    `json:"seq"`
	Text string `json:"text"`
	Good bool   `json:"good"`
}

var battleErrText = map[uint8]string{
	protocol.BattleErrNoLobby:    "That lobby doesn't exist any more.",
	protocol.BattleErrFull:       "That lobby is full.",
	protocol.BattleErrOtherRoom:  "That lobby is in another room.",
	protocol.BattleErrInLobby:    "You're already in a lobby. Leave it first.",
	protocol.BattleErrNotHost:    "Only the host can do that.",
	protocol.BattleErrNotReady:   "Everyone has to be ready first.",
	protocol.BattleErrStarted:    "That battle has already started.",
	protocol.BattleErrBadTurn:    "Out of sync with the battle.",
	protocol.BattleErrNotMember:  "You're not in that lobby.",
	protocol.BattleErrBadRequest: "Gyms and the Elite Four are solo or duo only (Champion up to 4).",
}

// handleLobbyPacket is called with p.mu held.
func (p *Player) handleLobbyPacket(t protocol.MsgType, payload []byte, logf func(string, ...any)) bool {
	switch t {
	case protocol.MsgRoomRoster:
		var m protocol.RoomRoster
		if m.Unmarshal(payload) == nil {
			p.roster = m.Entries
		}
	case protocol.MsgBattleLobby:
		var m protocol.BattleLobby
		if m.Unmarshal(payload) == nil {
			lobby := m
			p.lobby = &lobby
			delete(p.invites, m.LobbyID)
		}
	case protocol.MsgBattleInvite:
		var m protocol.BattleInvite
		if m.Unmarshal(payload) == nil {
			p.invites[m.LobbyID] = InviteView{LobbyID: m.LobbyID, From: p.nameOf(m.PlayerID), Kind: m.Kind, Target: m.Target}
			logf("%s invited you to %s", p.nameOf(m.PlayerID), BattleName(m.Kind, m.Target))
		}
	case protocol.MsgBattleStart:
		var m protocol.BattleStart
		if m.Unmarshal(payload) == nil && p.lobby != nil && p.lobby.LobbyID == m.LobbyID {
			p.lobby.State = protocol.LobbyInBattle
			p.note(fmt.Sprintf("%s started! Each of you, go battle them in your game.", BattleName(p.lobby.Kind, p.lobby.Target)), true)
		}
	case protocol.MsgBattleEnd:
		var m protocol.BattleEnd
		if m.Unmarshal(payload) == nil {
			name := "The battle"
			if p.lobby != nil && p.lobby.LobbyID == m.LobbyID {
				name = BattleName(p.lobby.Kind, p.lobby.Target)
				p.lobby = nil
			}
			if m.Outcome == protocol.OutcomeWin {
				p.note(name+" won! Badges recorded for the team.", true)
			} else {
				p.note(name+" lost. Try again!", false)
			}
		}
	case protocol.MsgBattleError:
		var m protocol.BattleError
		if m.Unmarshal(payload) == nil {
			text := battleErrText[m.Code]
			if text == "" {
				text = fmt.Sprintf("Battle error %d.", m.Code)
			}
			if m.Code == protocol.BattleErrNoLobby {
				delete(p.invites, m.LobbyID)
			}
			p.note(text, false)
		}
	default:
		return false
	}
	return true
}

func (p *Player) note(text string, good bool) {
	p.noteSeq++
	p.lastNote = BattleNote{Seq: p.noteSeq, Text: text, Good: good}
}

func (p *Player) nameOf(id uint16) string {
	for _, r := range p.roster {
		if r.PlayerID == id {
			return r.Name
		}
	}
	if pe := p.peers[id]; pe != nil {
		return pe.Name
	}
	if id == p.id {
		return "you"
	}
	return fmt.Sprintf("Player %d", id)
}

// battleView is called with p.mu held.
func (p *Player) battleView(v *PlayerView) {
	v.Roster = []RosterView{}
	for _, r := range p.roster {
		v.Roster = append(v.Roster, RosterView{ID: r.PlayerID, Name: r.Name, Zone: r.Zone, You: r.PlayerID == p.id})
	}
	v.Invites = []InviteView{}
	for _, inv := range p.invites {
		v.Invites = append(v.Invites, inv)
	}
	sort.Slice(v.Invites, func(i, j int) bool { return v.Invites[i].LobbyID < v.Invites[j].LobbyID })
	v.BattleNote = p.lastNote
	if l := p.lobby; l != nil {
		lv := &LobbyView{ID: l.LobbyID, Kind: l.Kind, Target: l.Target, MaxPlayers: l.MaxPlayers,
			InBattle: l.State == protocol.LobbyInBattle, YouAreHost: l.Host == p.id, Members: []LobbyMemberView{}}
		for _, m := range l.Members {
			lv.Members = append(lv.Members, LobbyMemberView{ID: m.PlayerID, Name: p.nameOf(m.PlayerID), Ready: m.Ready != 0,
				Helper: m.Completed != 0, You: m.PlayerID == p.id, IsHost: m.PlayerID == l.Host,
				Result: map[uint8]string{protocol.ReportWon: "won", protocol.ReportLost: "lost"}[m.Report]})
		}
		v.Lobby = lv
	}
}

// BattleAction performs one lobby action for the app.
func (p *Player) BattleAction(action string, kind uint8, target uint16, maxPlayers uint8, player uint16, lobby uint16, outcome uint8) error {
	if p.View().State != "online" {
		return fmt.Errorf("Join a room first.")
	}
	p.mu.Lock()
	cur := p.lobby
	p.mu.Unlock()
	curID := uint16(0)
	if cur != nil {
		curID = cur.LobbyID
	}
	send := func(t protocol.MsgType, b []byte) error { return p.client.Send(t, b) }
	switch action {
	case "create":
		return send(protocol.MsgBattleCreate, (&protocol.BattleCreate{Kind: kind, Target: target, MaxPlayers: maxPlayers}).Marshal())
	case "invite":
		return send(protocol.MsgBattleInvite, (&protocol.BattleInvite{LobbyID: curID, PlayerID: player}).Marshal())
	case "join":
		return send(protocol.MsgBattleJoin, (&protocol.LobbyRef{LobbyID: lobby}).Marshal())
	case "decline":
		p.mu.Lock()
		delete(p.invites, lobby)
		p.mu.Unlock()
		return nil
	case "ready", "unready":
		r := uint8(0)
		if action == "ready" {
			r = 1
		}
		return send(protocol.MsgBattleReady, (&protocol.BattleReady{LobbyID: curID, Ready: r}).Marshal())
	case "start":
		return send(protocol.MsgBattleStart, (&protocol.LobbyRef{LobbyID: curID}).Marshal())
	case "result":
		return send(protocol.MsgBattleResult, (&protocol.BattleResult{LobbyID: curID, Outcome: outcome}).Marshal())
	case "leave":
		err := send(protocol.MsgBattleLeave, (&protocol.LobbyRef{LobbyID: curID}).Marshal())
		p.mu.Lock()
		p.lobby = nil
		p.mu.Unlock()
		return err
	}
	return fmt.Errorf("unknown battle action")
}

var gymLeaders = []string{"Roark", "Gardenia", "Maylene", "Crasher Wake", "Fantina", "Byron", "Candice", "Volkner"}
var eliteFour = []string{"Aaron", "Bertha", "Flint", "Lucian"}

// BattleName is a friendly name like "Gym 1 (Roark)".
func BattleName(kind uint8, target uint16) string {
	switch kind {
	case protocol.BattleGym:
		if target >= 1 && int(target) <= len(gymLeaders) {
			return fmt.Sprintf("Gym %d (%s)", target, gymLeaders[target-1])
		}
		return fmt.Sprintf("Gym %d", target)
	case protocol.BattleEliteFour:
		if target >= 1 && int(target) <= len(eliteFour) {
			return fmt.Sprintf("Elite Four: %s", eliteFour[target-1])
		}
		return fmt.Sprintf("Elite Four %d", target)
	case protocol.BattleChampion:
		return "Champion Cynthia"
	}
	return "Battle"
}
