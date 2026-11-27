package coop

import (
	"context"
	"sort"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

// Status is a read-only snapshot of the server for dashboards (the launcher UI).
type Status struct {
	Uptime      time.Duration  `json:"-"`
	UptimeSec   int64          `json:"uptime_sec"`
	Connections int            `json:"connections"`
	Rooms       []RoomStatus   `json:"rooms"`
	Legends     []LegendStatus `json:"legends"`
}

type RoomStatus struct {
	Code    string         `json:"code"`
	Players []PlayerStatus `json:"players"`
	Lobbies int            `json:"lobbies"`
}

type PlayerStatus struct {
	ID       uint16 `json:"id"`
	Name     string `json:"name"`
	Zone     int32  `json:"zone"`
	Addr     string `json:"addr"`
	InBattle bool   `json:"in_battle"`
}

type LegendStatus struct {
	Room    string `json:"room"`
	Species uint16 `json:"species"`
	State   string `json:"state"` // "reserved" or "owned"
	Holder  string `json:"holder"`
}

type statusReq struct{ reply chan Status }

// Status asks the hub for a snapshot. Safe to call from any goroutine.
func (s *Server) Status(ctx context.Context) (Status, error) {
	r := statusReq{reply: make(chan Status, 1)}
	select {
	case s.statusCh <- r:
	case <-ctx.Done():
		return Status{}, ctx.Err()
	}
	select {
	case st := <-r.reply:
		return st, nil
	case <-ctx.Done():
		return Status{}, ctx.Err()
	}
}

func (s *Server) buildStatus() Status {
	st := Status{Uptime: time.Since(s.start), Connections: len(s.clients)}
	st.UptimeSec = int64(st.Uptime.Seconds())
	for _, r := range s.rooms {
		rs := RoomStatus{Code: r.code}
		for _, c := range r.players {
			rs.Players = append(rs.Players, PlayerStatus{ID: c.pid, Name: c.app.Name, Zone: c.zone, Addr: c.addr,
				InBattle: c.state.Flags&protocol.FlagInBattle != 0 || (c.lobby != nil && c.lobby.state == protocol.LobbyInBattle)})
		}
		sort.Slice(rs.Players, func(i, j int) bool { return rs.Players[i].ID < rs.Players[j].ID })
		for _, l := range s.lobbies {
			if l.room == r {
				rs.Lobbies++
			}
		}
		st.Rooms = append(st.Rooms, rs)
	}
	sort.Slice(st.Rooms, func(i, j int) bool { return st.Rooms[i].Code < st.Rooms[j].Code })
	for room, m := range s.store.AllLegends() {
		for sp, l := range m {
			st.Legends = append(st.Legends, LegendStatus{Room: room, Species: sp, State: "owned", Holder: l.OwnerName})
		}
	}
	for k, r := range s.reservations {
		st.Legends = append(st.Legends, LegendStatus{Room: k.room, Species: k.species, State: "reserved", Holder: r.name})
	}
	sort.Slice(st.Legends, func(i, j int) bool {
		a, b := st.Legends[i], st.Legends[j]
		if a.Room != b.Room {
			return a.Room < b.Room
		}
		return a.Species < b.Species
	})
	return st
}
