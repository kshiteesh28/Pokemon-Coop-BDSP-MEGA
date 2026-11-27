package coop

import (
	"context"
	"io"
	"log"
	"net"
	"testing"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/mockclient"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

const wait = 2 * time.Second

func startServer(t *testing.T, cfg Config) string {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Logger == nil {
		cfg.Logger = log.New(io.Discard, "", 0)
	}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	srv, err := New(cfg)
	if err != nil {
		t.Fatal(err)
	}
	go func() { srv.Serve(ctx, ln); close(done) }()
	t.Cleanup(func() { cancel(); <-done })
	return ln.Addr().String()
}

func join(t *testing.T, addr, room, name string, zone int32, pw string) (*mockclient.Client, protocol.HelloAck) {
	t.Helper()
	c, err := mockclient.Dial(addr)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.Close() })
	ack, err := c.Hello(protocol.HelloReq{RoomCode: room, Appearance: protocol.Appearance{Name: name, FollowSpecies: 25}, InitialZone: zone, Password: pw}, wait)
	if err != nil {
		t.Fatalf("%s hello: %v", name, err)
	}
	return c, ack
}

func waitMeta(t *testing.T, c *mockclient.Client) protocol.PlayerMeta {
	t.Helper()
	p, err := c.WaitFor(protocol.MsgPlayerMeta, wait)
	if err != nil {
		t.Fatal(err)
	}
	var m protocol.PlayerMeta
	if err := m.Unmarshal(p); err != nil {
		t.Fatal(err)
	}
	return m
}

func TestJoinMetaAndSnapshots(t *testing.T) {
	addr := startServer(t, Config{})
	a, ackA := join(t, addr, "test", "Alice", 100, "")
	b, ackB := join(t, addr, "TEST", "Bob", 100, "")
	if ackA.Status != protocol.StatusOK || ackB.Status != protocol.StatusOK || ackA.PlayerID == ackB.PlayerID {
		t.Fatalf("bad acks %+v %+v", ackA, ackB)
	}
	if m := waitMeta(t, a); m.PlayerID != ackB.PlayerID || m.Name != "Bob" || m.FollowSpecies != 25 {
		t.Fatalf("alice got meta %+v", m)
	}
	if m := waitMeta(t, b); m.PlayerID != ackA.PlayerID || m.Name != "Alice" {
		t.Fatalf("bob got meta %+v", m)
	}
	st := protocol.PlayerState{Zone: 100, X: 1.5, Y: 0, Z: -3, Yaw: 90, MoveSpeed: 2, AnimID: 7, FormState: 1, Flags: protocol.FlagInBattle}
	b.Send(protocol.MsgPlayerState, st.Marshal())
	deadline := time.Now().Add(wait)
	for time.Now().Before(deadline) {
		p, err := a.WaitFor(protocol.MsgWorldSnapshot, wait)
		if err != nil {
			t.Fatal(err)
		}
		var snap protocol.WorldSnapshot
		if err := snap.Unmarshal(p); err != nil {
			t.Fatal(err)
		}
		for _, e := range snap.Entries {
			if e.PlayerID == ackA.PlayerID {
				t.Fatal("snapshot included the receiver")
			}
			if e.PlayerID == ackB.PlayerID && e.State == st {
				return
			}
		}
	}
	t.Fatal("alice never saw bob's state")
}

func TestZoneChange(t *testing.T) {
	addr := startServer(t, Config{})
	a, _ := join(t, addr, "Z", "A", 100, "")
	b, ackB := join(t, addr, "Z", "B", 100, "")
	c, ackC := join(t, addr, "Z", "C", 101, "")
	waitMeta(t, a)
	waitMeta(t, b)
	// B walks from 100 to 101.
	b.Send(protocol.MsgZoneChange, (&protocol.ZoneChange{OldZone: 100, NewZone: 101}).Marshal())
	p, err := a.WaitFor(protocol.MsgPlayerLeave, wait)
	if err != nil {
		t.Fatal(err)
	}
	var l protocol.PlayerLeave
	l.Unmarshal(p)
	if l.PlayerID != ackB.PlayerID || l.Reason != protocol.LeaveZoneExit {
		t.Fatalf("leave %+v", l)
	}
	if m := waitMeta(t, b); m.PlayerID != ackC.PlayerID {
		t.Fatalf("b should get C's meta, got %+v", m)
	}
	if m := waitMeta(t, c); m.PlayerID != ackB.PlayerID || m.Zone != 101 {
		t.Fatalf("c should get B's meta, got %+v", m)
	}
	// Implicit zone change via PLAYER_STATE: A moves to 101 too.
	a.Send(protocol.MsgPlayerState, (&protocol.PlayerState{Zone: 101}).Marshal())
	if m := waitMeta(t, c); m.Name != "A" {
		t.Fatalf("c should get A's meta, got %+v", m)
	}
	// Resync: same-zone ZONE_CHANGE resends peers' metas.
	c.Send(protocol.MsgZoneChange, (&protocol.ZoneChange{OldZone: 101, NewZone: 101}).Marshal())
	seen := map[string]bool{}
	for len(seen) < 2 {
		seen[waitMeta(t, c).Name] = true
	}
}

func TestRoomFullAndPassword(t *testing.T) {
	addr := startServer(t, Config{MaxPlayersPerRoom: 2, Password: "hunter2"})
	_, ack := join(t, addr, "R", "A", 1, "wrong")
	if ack.Status != protocol.StatusBadPassword {
		t.Fatalf("want bad password, got %d", ack.Status)
	}
	join(t, addr, "R", "A", 1, "hunter2")
	join(t, addr, "R", "B", 1, "hunter2")
	_, ack = join(t, addr, "R", "C", 1, "hunter2")
	if ack.Status != protocol.StatusRoomFull {
		t.Fatalf("want room full, got %d", ack.Status)
	}
	_, ack = join(t, addr, "OTHER", "C", 1, "hunter2")
	if ack.Status != protocol.StatusOK {
		t.Fatalf("other room should accept, got %d", ack.Status)
	}
	_, ack = join(t, addr, "bad room!", "D", 1, "hunter2")
	if ack.Status != protocol.StatusBadRoomCode {
		t.Fatalf("want bad room code, got %d", ack.Status)
	}
}

func TestTimeoutEvictsSilentClient(t *testing.T) {
	addr := startServer(t, Config{Timeout: 700 * time.Millisecond})
	a, _ := join(t, addr, "T", "A", 5, "")
	_, ackB := join(t, addr, "T", "B", 5, "") // B goes silent
	waitMeta(t, a)
	stop := make(chan struct{})
	defer close(stop)
	go func() { // A keeps pinging
		for {
			select {
			case <-stop:
				return
			case <-time.After(200 * time.Millisecond):
				a.Send(protocol.MsgPing, (&protocol.PingPong{TimestampMs: 1}).Marshal())
			}
		}
	}()
	p, err := a.WaitFor(protocol.MsgPlayerLeave, 3*time.Second)
	if err != nil {
		t.Fatal(err)
	}
	var l protocol.PlayerLeave
	l.Unmarshal(p)
	if l.PlayerID != ackB.PlayerID || l.Reason != protocol.LeaveTimeout {
		t.Fatalf("leave %+v", l)
	}
}

func TestDisconnectNotifiesRoom(t *testing.T) {
	addr := startServer(t, Config{})
	a, _ := join(t, addr, "D", "A", 1, "")
	b, ackB := join(t, addr, "D", "B", 2, "") // different zone, same room
	b.Close()
	p, err := a.WaitFor(protocol.MsgPlayerLeave, wait)
	if err != nil {
		t.Fatal(err)
	}
	var l protocol.PlayerLeave
	l.Unmarshal(p)
	if l.PlayerID != ackB.PlayerID || l.Reason != protocol.LeaveDisconnect {
		t.Fatalf("leave %+v", l)
	}
}

func TestCoopEventRelay(t *testing.T) {
	addr := startServer(t, Config{})
	a, ackA := join(t, addr, "E", "A", 1, "")
	b, ackB := join(t, addr, "E", "B", 9, "")
	c, _ := join(t, addr, "E", "C", 9, "")
	a.Send(protocol.MsgCoopEvent, (&protocol.CoopEvent{PlayerID: ackB.PlayerID, EventType: 2, Data: []byte("trade?")}).Marshal())
	p, err := b.WaitFor(protocol.MsgCoopEvent, wait)
	if err != nil {
		t.Fatal(err)
	}
	var e protocol.CoopEvent
	e.Unmarshal(p)
	if e.PlayerID != ackA.PlayerID || e.EventType != 2 || string(e.Data) != "trade?" {
		t.Fatalf("event %+v", e)
	}
	a.Send(protocol.MsgCoopEvent, (&protocol.CoopEvent{PlayerID: protocol.BroadcastTarget, EventType: 1, Data: []byte("hi")}).Marshal())
	if _, err := c.WaitFor(protocol.MsgCoopEvent, wait); err != nil {
		t.Fatal("broadcast not received:", err)
	}
}

func TestPingAndGarbage(t *testing.T) {
	addr := startServer(t, Config{})
	c, err := mockclient.Dial(addr)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	c.Send(protocol.MsgPing, (&protocol.PingPong{TimestampMs: 1234}).Marshal())
	p, err := c.WaitFor(protocol.MsgPong, wait)
	if err != nil {
		t.Fatal(err)
	}
	var pp protocol.PingPong
	pp.Unmarshal(p)
	if pp.TimestampMs != 1234 {
		t.Fatalf("pong %d", pp.TimestampMs)
	}
	// State before HELLO -> disconnected.
	c.Send(protocol.MsgPlayerState, (&protocol.PlayerState{}).Marshal())
	if !c.WaitClosed(wait) {
		t.Fatal("expected disconnect for PLAYER_STATE before HELLO")
	}
	// Bad magic -> disconnected.
	d, _ := mockclient.Dial(addr)
	defer d.Close()
	d.Conn.Write([]byte("GET / HTTP/1.1\r\n\r\n"))
	if !d.WaitClosed(wait) {
		t.Fatal("expected disconnect for garbage")
	}
}

func TestBadFloatRejected(t *testing.T) {
	var st protocol.PlayerState
	b := (&protocol.PlayerState{X: 1}).Marshal()
	b[4], b[5], b[6], b[7] = 0, 0, 0xc0, 0x7f // NaN in pos_x
	if err := st.Unmarshal(b); err == nil {
		t.Fatal("NaN accepted")
	}
}
