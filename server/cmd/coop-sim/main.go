// coop-sim spawns mock players that walk in circles and hop between zones 100
// and 101, then reports what each one received (guide §6.7 Phase 1).
//
//	coop-sim -addr 127.0.0.1:4100 -players 16 -per-room 4 -duration 20s
//	coop-sim -config coop.ini -players 1 -duration 0   # stay connected until Ctrl+C
//
// Settings come from flags first, then coop.ini (address, room, name, password),
// then the built-in defaults.
package main

import (
	"context"
	"flag"
	"fmt"
	"math"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/config"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/mockclient"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

type stats struct {
	live                     bool // print joins/leaves as they happen (single-player mode)
	name                     string
	snapshots, metas, leaves int
	pongs                    int
	rttSum                   time.Duration
	err                      error
}

func main() {
	addr := flag.String("addr", "127.0.0.1:4100", "server address")
	players := flag.Int("players", 16, "mock players to spawn")
	perRoom := flag.Int("per-room", 4, "players per room")
	duration := flag.Duration("duration", 20*time.Second, "how long to run (0 = until Ctrl+C)")
	password := flag.String("password", os.Getenv("COOP_PASSWORD"), "server password")
	nameFlag := flag.String("name", "Bot", "trainer name prefix shown on the server (e.g. your own name)")
	roomFlag := flag.String("room", "", "join this room code (default: SIM-1, SIM-2, ... filled -per-room at a time)")
	cfgPath := flag.String("config", "coop.ini", "settings file; flags given on the command line override it")
	flag.Parse()

	set := map[string]bool{}
	flag.Visit(func(f *flag.Flag) { set[f.Name] = true })
	cfg, found, err := config.LoadClient(*cfgPath)
	if err != nil {
		fmt.Fprintln(os.Stderr, "error:", err)
		os.Exit(2)
	}
	if found {
		apply := func(flagName, value string, dst *string) {
			if !set[flagName] && value != "" {
				*dst = value
			}
		}
		apply("addr", cfg.Address, addr)
		apply("room", cfg.Room, roomFlag)
		apply("name", cfg.Name, nameFlag)
		apply("password", cfg.Password, password)
		fmt.Printf("using settings from %s\n", *cfgPath)
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if *duration > 0 {
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(ctx, *duration)
		defer cancel()
	} else {
		fmt.Println("connected players stay online until you press Ctrl+C")
	}
	fmt.Printf("connecting to %s as %q\n", *addr, *nameFlag)

	roomFor := func(i int) string {
		if *roomFlag != "" {
			return *roomFlag
		}
		return fmt.Sprintf("SIM-%d", i / *perRoom + 1)
	}
	start := time.Now()
	results := make([]*stats, *players)
	var wg sync.WaitGroup
	for i := 0; i < *players; i++ {
		wg.Add(1)
		name := *nameFlag
		if *players > 1 {
			name = fmt.Sprintf("%s%02d", *nameFlag, i+1)
		}
		results[i] = &stats{name: name, live: *players == 1}
		go func(i int) {
			defer wg.Done()
			run(ctx, *addr, roomFor(i), i, *password, start, results[i])
		}(i)
		time.Sleep(20 * time.Millisecond)
	}
	wg.Wait()

	failed := 0
	fmt.Printf("%-16s %-10s %9s %6s %7s %8s\n", "player", "room", "snapshots", "metas", "leaves", "avg rtt")
	for i, r := range results {
		rtt := "-"
		if r.pongs > 0 {
			rtt = (r.rttSum / time.Duration(r.pongs)).Round(100 * time.Microsecond).String()
		}
		fmt.Printf("%-16s %-10s %9d %6d %7d %8s", r.name, roomFor(i), r.snapshots, r.metas, r.leaves, rtt)
		if r.err != nil {
			fmt.Printf("  ERROR: %v", r.err)
			failed++
		} else if r.snapshots == 0 && *perRoom > 1 && *roomFlag == "" {
			fmt.Printf("  ERROR: no snapshots")
			failed++
		}
		fmt.Println()
	}
	if failed > 0 {
		fmt.Printf("%d of %d bots failed\n", failed, *players)
		os.Exit(1)
	}
	fmt.Println("all bots OK")
}

var statusText = map[uint8]string{
	protocol.StatusRoomFull:    "room is full (4 players max)",
	protocol.StatusBadPassword: "wrong password",
	protocol.StatusBadVersion:  "server and client versions don't match; update both",
	protocol.StatusServerFull:  "server has too many rooms open",
	protocol.StatusBadRoomCode: "room code may only use letters, numbers, - and _",
}

func run(ctx context.Context, addr, room string, i int, pw string, start time.Time, st *stats) {
	c, err := mockclient.Dial(addr)
	if err != nil {
		st.err = fmt.Errorf("can't reach the server at %s (is it running, and are you on the same Wi-Fi or Tailscale?): %v", addr, err)
		return
	}
	defer c.Close()
	zone := int32(100 + i%2)
	if st.live { // one person testing with friends: everyone stays on the same map
		zone = 100
	}
	ack, err := c.Hello(protocol.HelloReq{RoomCode: room, Appearance: protocol.Appearance{Name: st.name, FashionID: uint16(i % 8), FollowSpecies: uint16(1 + i)}, InitialZone: zone, Password: pw}, 5*time.Second)
	if err != nil {
		st.err = err
		return
	}
	if ack.Status != protocol.StatusOK {
		msg := statusText[ack.Status]
		if msg == "" {
			msg = fmt.Sprintf("status %d", ack.Status)
		}
		st.err = fmt.Errorf("server refused to join: %s", msg)
		return
	}
	if st.live {
		fmt.Printf("joined room %s as player #%d\n", room, ack.PlayerID)
	}
	names := map[uint16]string{}

	done := make(chan struct{})
	go func() { // consume incoming packets
		for p := range c.Packets {
			switch p.Type {
			case protocol.MsgWorldSnapshot:
				st.snapshots++
			case protocol.MsgPlayerMeta:
				st.metas++
				var m protocol.PlayerMeta
				if st.live && m.Unmarshal(p.Payload) == nil {
					if _, seen := names[m.PlayerID]; !seen {
						fmt.Printf("  + %s is on your map\n", m.Name)
					}
					names[m.PlayerID] = m.Name
				}
			case protocol.MsgPlayerLeave:
				st.leaves++
				var m protocol.PlayerLeave
				if st.live && m.Unmarshal(p.Payload) == nil && names[m.PlayerID] != "" {
					why := map[uint8]string{protocol.LeaveZoneExit: "walked to another map", protocol.LeaveDisconnect: "left the game", protocol.LeaveTimeout: "lost connection"}[m.Reason]
					fmt.Printf("  - %s %s\n", names[m.PlayerID], why)
					delete(names, m.PlayerID)
				}
			case protocol.MsgPong:
				var pp protocol.PingPong
				if pp.Unmarshal(p.Payload) == nil {
					st.pongs++
					st.rttSum += time.Since(start) - time.Duration(pp.TimestampMs)*time.Millisecond
				}
			}
		}
		close(done)
	}()

	tick := time.NewTicker(50 * time.Millisecond)
	defer tick.Stop()
	end := ctx.Done()
	phase := float64(i)
	nextHop := time.Now().Add(time.Duration(3+i%5) * time.Second)
	if st.live {
		nextHop = time.Now().Add(100 * 365 * 24 * time.Hour)
	}
	lastPing := time.Time{}
	for {
		select {
		case <-end:
			c.Close()
			<-done
			return
		case now := <-tick.C:
			if now.After(nextHop) { // walk through a door into the other zone
				old := zone
				zone = 201 - zone
				c.Send(protocol.MsgZoneChange, (&protocol.ZoneChange{OldZone: old, NewZone: zone}).Marshal())
				nextHop = now.Add(time.Duration(4+i%5) * time.Second)
			}
			t := now.Sub(start).Seconds()
			a := t*1.5 + phase
			s := protocol.PlayerState{Zone: zone, X: float32(math.Cos(a) * 5), Z: float32(math.Sin(a) * 5),
				Yaw: float32(math.Mod(a*180/math.Pi+90, 360)), MoveSpeed: 3, FormState: 1}
			if err := c.Send(protocol.MsgPlayerState, s.Marshal()); err != nil {
				st.err = err
				return
			}
			if now.Sub(lastPing) > 2*time.Second {
				lastPing = now
				c.Send(protocol.MsgPing, (&protocol.PingPong{TimestampMs: uint32(now.Sub(start).Milliseconds())}).Marshal())
			}
		}
	}
}
