// coop-sim spawns mock players that walk in circles and hop between zones 100
// and 101, then reports what each one received (guide §6.7 Phase 1).
//
//	coop-sim -addr 127.0.0.1:4100 -players 16 -per-room 4 -duration 20s
package main

import (
	"flag"
	"fmt"
	"math"
	"os"
	"sync"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/mockclient"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

type stats struct {
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
	duration := flag.Duration("duration", 20*time.Second, "how long to run")
	password := flag.String("password", os.Getenv("COOP_PASSWORD"), "server password")
	roomFlag := flag.String("room", "", "join this room code (default: SIM-1, SIM-2, ... filled -per-room at a time)")
	flag.Parse()

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
		results[i] = &stats{name: fmt.Sprintf("Bot%02d", i+1)}
		go func(i int) {
			defer wg.Done()
			run(*addr, roomFor(i), i, *password, *duration, start, results[i])
		}(i)
		time.Sleep(20 * time.Millisecond)
	}
	wg.Wait()

	failed := 0
	fmt.Printf("%-6s %-8s %9s %6s %7s %8s\n", "bot", "room", "snapshots", "metas", "leaves", "avg rtt")
	for i, r := range results {
		rtt := "-"
		if r.pongs > 0 {
			rtt = (r.rttSum / time.Duration(r.pongs)).Round(100 * time.Microsecond).String()
		}
		fmt.Printf("%-6s %-8s %9d %6d %7d %8s", r.name, roomFor(i), r.snapshots, r.metas, r.leaves, rtt)
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

func run(addr, room string, i int, pw string, d time.Duration, start time.Time, st *stats) {
	c, err := mockclient.Dial(addr)
	if err != nil {
		st.err = err
		return
	}
	defer c.Close()
	zone := int32(100 + i%2)
	ack, err := c.Hello(protocol.HelloReq{RoomCode: room, Appearance: protocol.Appearance{Name: st.name, FashionID: uint16(i % 8), FollowSpecies: uint16(1 + i)}, InitialZone: zone, Password: pw}, 5*time.Second)
	if err != nil {
		st.err = err
		return
	}
	if ack.Status != protocol.StatusOK {
		st.err = fmt.Errorf("HELLO rejected, status %d", ack.Status)
		return
	}

	done := make(chan struct{})
	go func() { // consume incoming packets
		for p := range c.Packets {
			switch p.Type {
			case protocol.MsgWorldSnapshot:
				st.snapshots++
			case protocol.MsgPlayerMeta:
				st.metas++
			case protocol.MsgPlayerLeave:
				st.leaves++
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
	end := time.After(d)
	phase := float64(i)
	nextHop := time.Now().Add(time.Duration(3+i%5) * time.Second)
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
