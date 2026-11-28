package launcher

import (
	"context"
	"fmt"
	"math"
	"sort"
	"sync"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/config"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/coop"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/mockclient"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

// Player is a stand-in trainer that joins a server from the launcher, walks a
// small circle on map 100 and reports who else it can see. It is what the real
// game will do once the in-game client exists.
type Player struct {
	cancel context.CancelFunc
	done   chan struct{}
	client *mockclient.Client

	mu       sync.Mutex
	state    string // "online" or "offline"
	reason   string
	id       uint16
	room     string
	addr     string
	rttMs    int
	peers    map[uint16]*peer
	progress []protocol.ProgressEntry
	legends  map[uint16]protocol.LegendUpdate
	chat     []ChatLine
	chatSeq  int
	roster   []protocol.RosterEntry
	lobby    *protocol.BattleLobby
	invites  map[uint16]InviteView
	lastNote BattleNote
	noteSeq  int
}

// ChatLine is one room chat message. System lines (rate-limit warnings) have From == "".
type ChatLine struct {
	Seq  int    `json:"seq"`
	Time string `json:"time"`
	From string `json:"from"`
	Mine bool   `json:"mine"`
	Text string `json:"text"`
}

type peer struct {
	Name   string
	Zone   int32
	X, Z   float32
	Seen   time.Time
	HasPos bool
}

type PeerView struct {
	ID     uint16  `json:"id"`
	Name   string  `json:"name"`
	Zone   int32   `json:"zone"`
	Moving bool    `json:"moving"`
	Dist   float64 `json:"dist"`
}

type PlayerView struct {
	State      string                   `json:"state"`
	Reason     string                   `json:"reason,omitempty"`
	ID         uint16                   `json:"id"`
	Room       string                   `json:"room"`
	Addr       string                   `json:"addr"`
	RttMs      int                      `json:"rtt_ms"`
	Peers      []PeerView               `json:"peers"`
	Progress   []protocol.ProgressEntry `json:"progress"`
	Legends    []LegendView             `json:"legends"`
	Chat       []ChatLine               `json:"chat"`
	Roster     []RosterView             `json:"roster"`
	Lobby      *LobbyView               `json:"lobby"`
	Invites    []InviteView             `json:"invites"`
	BattleNote BattleNote               `json:"battle_note"`
}

type LegendView struct {
	Species uint16 `json:"species"`
	State   string `json:"state"`
	Holder  string `json:"holder"`
}

var helloStatus = map[uint8]string{
	protocol.StatusRoomFull:    "That room is full (4 players max).",
	protocol.StatusBadPassword: "Wrong password.",
	protocol.StatusBadVersion:  "The server is a different version. Update both sides.",
	protocol.StatusServerFull:  "The server has too many rooms open.",
	protocol.StatusBadRoomCode: "Room codes can only use letters, numbers, - and _.",
}

// JoinServer connects and completes HELLO, returning a friendly error if it can't.
func JoinServer(cfg config.Client, logf func(string, ...any)) (*Player, error) {
	if cfg.Address == "" {
		return nil, fmt.Errorf("Enter the server address first.")
	}
	if cfg.Name == "" {
		return nil, fmt.Errorf("Enter your trainer name first.")
	}
	c, err := mockclient.Dial(cfg.Address)
	if err != nil {
		return nil, fmt.Errorf("Can't reach %s. Is the host's server running, and are you on the same Wi-Fi or Tailscale?", cfg.Address)
	}
	ack, err := c.Hello(protocol.HelloReq{RoomCode: cfg.Room, Appearance: protocol.Appearance{Name: cfg.Name}, InitialZone: 100, Password: cfg.Password}, 5*time.Second)
	if err != nil {
		c.Close()
		return nil, fmt.Errorf("The server didn't answer. Check the address and port.")
	}
	if ack.Status != protocol.StatusOK {
		c.Close()
		msg := helloStatus[ack.Status]
		if msg == "" {
			msg = fmt.Sprintf("The server refused (status %d).", ack.Status)
		}
		return nil, fmt.Errorf("%s", msg)
	}
	room, _ := coop.NormalizeRoomCode(cfg.Room)
	ctx, cancel := context.WithCancel(context.Background())
	p := &Player{cancel: cancel, done: make(chan struct{}), client: c, state: "online", id: ack.PlayerID, room: room, addr: cfg.Address,
		peers: map[uint16]*peer{}, legends: map[uint16]protocol.LegendUpdate{}, invites: map[uint16]InviteView{}}
	logf("Joined room %s on %s as #%d", room, cfg.Address, ack.PlayerID)
	go p.run(ctx, c, logf)
	return p, nil
}

const maxChat = 100

// Say sends a chat message to everyone in the room.
func (p *Player) Say(text string) error {
	if p.View().State != "online" {
		return fmt.Errorf("You're not connected.")
	}
	return p.client.Send(protocol.MsgChat, (&protocol.ChatSend{Text: text}).Marshal())
}

func (p *Player) Leave() {
	p.cancel()
	<-p.done
}

func (p *Player) View() PlayerView {
	p.mu.Lock()
	defer p.mu.Unlock()
	v := PlayerView{State: p.state, Reason: p.reason, ID: p.id, Room: p.room, Addr: p.addr, RttMs: p.rttMs,
		Progress: append([]protocol.ProgressEntry{}, p.progress...), Peers: []PeerView{}, Legends: []LegendView{},
		Chat: append([]ChatLine{}, p.chat...)}
	p.battleView(&v)
	for id, pe := range p.peers {
		dist := -1.0
		if pe.HasPos {
			dist = math.Hypot(float64(pe.X), float64(pe.Z))
		}
		v.Peers = append(v.Peers, PeerView{ID: id, Name: pe.Name, Zone: pe.Zone, Moving: time.Since(pe.Seen) < 2*time.Second, Dist: dist})
	}
	sort.Slice(v.Peers, func(i, j int) bool { return v.Peers[i].ID < v.Peers[j].ID })
	for sp, l := range p.legends {
		if l.State == protocol.LegendFree {
			continue
		}
		st := "owned"
		if l.State == protocol.LegendReserved {
			st = "reserved"
		}
		v.Legends = append(v.Legends, LegendView{Species: sp, State: st, Holder: l.OwnerName})
	}
	sort.Slice(v.Legends, func(i, j int) bool { return v.Legends[i].Species < v.Legends[j].Species })
	return v
}

func (p *Player) run(ctx context.Context, c *mockclient.Client, logf func(string, ...any)) {
	defer close(p.done)
	defer c.Close()
	start := time.Now()
	tick := time.NewTicker(50 * time.Millisecond)
	defer tick.Stop()
	var lastPing time.Time
	for {
		select {
		case <-ctx.Done():
			p.setOffline("You left.")
			logf("You left room %s", p.room)
			return
		case pkt, ok := <-c.Packets:
			if !ok {
				p.setOffline("Lost connection to the server.")
				logf("Lost connection to %s", p.addr)
				return
			}
			p.handle(pkt, start, logf)
		case now := <-tick.C:
			a := now.Sub(start).Seconds() * 1.2
			st := protocol.PlayerState{Zone: 100, X: float32(math.Cos(a) * 4), Z: float32(math.Sin(a) * 4), Yaw: float32(math.Mod(a*180/math.Pi+90, 360)), MoveSpeed: 2}
			if c.Send(protocol.MsgPlayerState, st.Marshal()) != nil {
				continue
			}
			if now.Sub(lastPing) > 2*time.Second {
				lastPing = now
				c.Send(protocol.MsgPing, (&protocol.PingPong{TimestampMs: uint32(now.Sub(start).Milliseconds())}).Marshal())
			}
		}
	}
}

func (p *Player) setOffline(reason string) {
	p.mu.Lock()
	p.state, p.reason = "offline", reason
	p.peers = map[uint16]*peer{}
	p.roster, p.lobby, p.invites = nil, nil, map[uint16]InviteView{}
	p.mu.Unlock()
}

func (p *Player) handle(pkt mockclient.Packet, start time.Time, logf func(string, ...any)) {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.handleLobbyPacket(pkt.Type, pkt.Payload, logf) {
		return
	}
	switch pkt.Type {
	case protocol.MsgPlayerMeta:
		var m protocol.PlayerMeta
		if m.Unmarshal(pkt.Payload) == nil {
			if _, known := p.peers[m.PlayerID]; !known {
				logf("%s is on your map", m.Name)
			}
			p.peers[m.PlayerID] = &peer{Name: m.Name, Zone: m.Zone}
		}
	case protocol.MsgPlayerLeave:
		var m protocol.PlayerLeave
		if m.Unmarshal(pkt.Payload) == nil {
			if pe := p.peers[m.PlayerID]; pe != nil {
				why := map[uint8]string{protocol.LeaveZoneExit: "walked to another map", protocol.LeaveDisconnect: "left the game", protocol.LeaveTimeout: "lost connection"}[m.Reason]
				logf("%s %s", pe.Name, why)
				delete(p.peers, m.PlayerID)
			}
		}
	case protocol.MsgWorldSnapshot:
		var s protocol.WorldSnapshot
		if s.Unmarshal(pkt.Payload) == nil {
			now := time.Now()
			for _, e := range s.Entries {
				if pe := p.peers[e.PlayerID]; pe != nil {
					pe.X, pe.Z, pe.Zone, pe.Seen, pe.HasPos = e.State.X, e.State.Z, e.State.Zone, now, true
				}
			}
		}
	case protocol.MsgPong:
		var m protocol.PingPong
		if m.Unmarshal(pkt.Payload) == nil {
			p.rttMs = int(time.Since(start).Milliseconds()) - int(m.TimestampMs)
		}
	case protocol.MsgChat:
		var m protocol.ChatMessage
		if m.Unmarshal(pkt.Payload) == nil {
			p.chatSeq++
			from := m.Name
			if m.PlayerID == 0 {
				from = ""
			}
			p.chat = append(p.chat, ChatLine{Seq: p.chatSeq, Time: time.Now().Format("15:04"), From: from, Mine: m.PlayerID == p.id, Text: m.Text})
			if len(p.chat) > maxChat {
				p.chat = p.chat[len(p.chat)-maxChat:]
			}
		}
	case protocol.MsgProgress:
		var m protocol.Progress
		if m.Unmarshal(pkt.Payload) == nil {
			p.progress = m.Entries
		}
	case protocol.MsgLegendUpdate:
		var m protocol.LegendUpdate
		if m.Unmarshal(pkt.Payload) == nil {
			p.legends[m.Species] = m
		}
	}
}
