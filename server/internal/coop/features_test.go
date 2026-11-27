package coop

import (
	"context"
	"io"
	"log"
	"net"
	"path/filepath"
	"testing"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/mockclient"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

const giratina = 487

type unmarshaler interface{ Unmarshal([]byte) error }

func recv(t *testing.T, c *mockclient.Client, typ protocol.MsgType, into unmarshaler) {
	t.Helper()
	p, err := c.WaitFor(typ, wait)
	if err != nil {
		t.Fatal(err)
	}
	if err := into.Unmarshal(p); err != nil {
		t.Fatal(err)
	}
}

func joinID(t *testing.T, addr, room, name string, tid uint32) (*mockclient.Client, protocol.HelloAck) {
	t.Helper()
	c, err := mockclient.Dial(addr)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.Close() })
	ack, err := c.Hello(protocol.HelloReq{RoomCode: room, Appearance: protocol.Appearance{Name: name}, InitialZone: 1, TrainerID: tid, SecretID: tid * 7}, wait)
	if err != nil || ack.Status != protocol.StatusOK {
		t.Fatalf("%s hello: %v %+v", name, err, ack)
	}
	return c, ack
}

func reserve(t *testing.T, c *mockclient.Client, sp uint16) protocol.LegendReserveAck {
	t.Helper()
	c.Send(protocol.MsgLegendReserve, (&protocol.LegendReserve{Species: sp}).Marshal())
	var a protocol.LegendReserveAck
	recv(t, c, protocol.MsgLegendReserveAck, &a)
	return a
}

func expectUpdate(t *testing.T, c *mockclient.Client, sp uint16, state uint8, owner string) {
	t.Helper()
	deadline := time.Now().Add(wait)
	for time.Now().Before(deadline) {
		var u protocol.LegendUpdate
		recv(t, c, protocol.MsgLegendUpdate, &u)
		if u.Species == sp && u.State == state && u.OwnerName == owner {
			return
		}
	}
	t.Fatalf("no LEGEND_UPDATE species=%d state=%d owner=%q", sp, state, owner)
}

func TestLegendaryLifecycle(t *testing.T) {
	addr := startServer(t, Config{})
	a, _ := joinID(t, addr, "L", "Ash", 1)
	b, ackB := joinID(t, addr, "L", "Brock", 2)

	if r := reserve(t, a, giratina); r.Status != protocol.LegendGranted {
		t.Fatalf("A reserve: %+v", r)
	}
	expectUpdate(t, b, giratina, protocol.LegendReserved, "Ash")
	if r := reserve(t, b, giratina); r.Status != protocol.LegendReservedOther || r.HolderName != "Ash" {
		t.Fatalf("B should be blocked: %+v", r)
	}
	// A runs away -> free again.
	a.Send(protocol.MsgLegendResult, (&protocol.LegendResult{Species: giratina, Result: protocol.LegendEscaped}).Marshal())
	expectUpdate(t, b, giratina, protocol.LegendFree, "")
	// B catches it.
	if r := reserve(t, b, giratina); r.Status != protocol.LegendGranted {
		t.Fatalf("B reserve: %+v", r)
	}
	b.Send(protocol.MsgLegendResult, (&protocol.LegendResult{Species: giratina, Result: protocol.LegendCaught}).Marshal())
	expectUpdate(t, a, giratina, protocol.LegendOwned, "Brock")
	if r := reserve(t, a, giratina); r.Status != protocol.LegendOwnedByOther || r.HolderName != "Brock" {
		t.Fatalf("A should see owned: %+v", r)
	}
	if r := reserve(t, b, giratina); r.Status != protocol.LegendYouOwn {
		t.Fatalf("B owns it: %+v", r)
	}
	// Non-legendary is untracked.
	if r := reserve(t, a, 25); r.Status != protocol.LegendUntracked {
		t.Fatalf("pikachu: %+v", r)
	}
	// Only the owner can trade it; then ownership moves.
	_ = ackB
	b.Send(protocol.MsgLegendTransfer, (&protocol.LegendTransfer{Species: giratina, ToPlayer: 1}).Marshal())
	expectUpdate(t, a, giratina, protocol.LegendOwned, "Ash")
	if r := reserve(t, a, giratina); r.Status != protocol.LegendYouOwn {
		t.Fatalf("A should own after trade: %+v", r)
	}
	// Release frees it.
	a.Send(protocol.MsgLegendResult, (&protocol.LegendResult{Species: giratina, Result: protocol.LegendReleased}).Marshal())
	expectUpdate(t, b, giratina, protocol.LegendFree, "")
}

func TestLegendaryIsPerRoom(t *testing.T) {
	addr := startServer(t, Config{})
	a, _ := joinID(t, addr, "ROOM-A", "Ash", 1)
	b, _ := joinID(t, addr, "ROOM-B", "Brock", 2)
	reserve(t, a, giratina)
	a.Send(protocol.MsgLegendResult, (&protocol.LegendResult{Species: giratina, Result: protocol.LegendCaught}).Marshal())
	if r := reserve(t, b, giratina); r.Status != protocol.LegendGranted {
		t.Fatalf("other room should be independent: %+v", r)
	}
}

func TestLegendaryPersistsAcrossRestart(t *testing.T) {
	path := filepath.Join(t.TempDir(), "data.json")
	run := func(f func(addr string)) {
		ln, _ := net.Listen("tcp", "127.0.0.1:0")
		srv, err := New(Config{DataPath: path, Logger: log.New(io.Discard, "", 0)})
		if err != nil {
			t.Fatal(err)
		}
		ctx, cancel := context.WithCancel(context.Background())
		done := make(chan struct{})
		go func() { srv.Serve(ctx, ln); close(done) }()
		f(ln.Addr().String())
		cancel()
		<-done
	}
	run(func(addr string) {
		a, _ := joinID(t, addr, "P", "Ash", 1)
		reserve(t, a, giratina)
		a.Send(protocol.MsgLegendResult, (&protocol.LegendResult{Species: giratina, Result: protocol.LegendCaught}).Marshal())
		expectUpdate(t, a, giratina, protocol.LegendOwned, "Ash")
	})
	run(func(addr string) {
		b, _ := joinID(t, addr, "P", "Brock", 2)
		expectUpdate(t, b, giratina, protocol.LegendOwned, "Ash") // sent on join
		// Same identity reconnecting still owns it.
		a, _ := joinID(t, addr, "P", "Ash", 1)
		if r := reserve(t, a, giratina); r.Status != protocol.LegendYouOwn {
			t.Fatalf("Ash should still own it: %+v", r)
		}
	})
}

func TestReservationReleasedAfterDisconnectGrace(t *testing.T) {
	addr := startServer(t, Config{ReserveGrace: 300 * time.Millisecond})
	a, _ := joinID(t, addr, "G", "Ash", 1)
	b, _ := joinID(t, addr, "G", "Brock", 2)
	reserve(t, a, giratina)
	a.Close()
	if r := reserve(t, b, giratina); r.Status != protocol.LegendReservedOther {
		t.Fatalf("still in grace period: %+v", r)
	}
	expectUpdate(t, b, giratina, protocol.LegendFree, "")
	if r := reserve(t, b, giratina); r.Status != protocol.LegendGranted {
		t.Fatalf("after grace: %+v", r)
	}
}

func lobbyOf(t *testing.T, c *mockclient.Client) protocol.BattleLobby {
	t.Helper()
	var l protocol.BattleLobby
	recv(t, c, protocol.MsgBattleLobby, &l)
	return l
}

func battleErr(t *testing.T, c *mockclient.Client) uint8 {
	t.Helper()
	var e protocol.BattleError
	recv(t, c, protocol.MsgBattleError, &e)
	return e.Code
}

func TestGymDuoBattle(t *testing.T) {
	addr := startServer(t, Config{})
	a, ackA := joinID(t, addr, "B", "Ash", 1)
	b, ackB := joinID(t, addr, "B", "Brock", 2)
	c, _ := joinID(t, addr, "B", "Misty", 3)

	// Gyms are solo or duo only.
	a.Send(protocol.MsgBattleCreate, (&protocol.BattleCreate{Kind: protocol.BattleGym, Target: 3, MaxPlayers: 3}).Marshal())
	if code := battleErr(t, a); code != protocol.BattleErrBadRequest {
		t.Fatalf("3-player gym should be refused, got %d", code)
	}
	a.Send(protocol.MsgBattleCreate, (&protocol.BattleCreate{Kind: protocol.BattleGym, Target: 3, MaxPlayers: 2}).Marshal())
	l := lobbyOf(t, a)
	if l.Host != ackA.PlayerID || len(l.Members) != 1 {
		t.Fatalf("lobby %+v", l)
	}
	a.Send(protocol.MsgBattleInvite, (&protocol.BattleInvite{LobbyID: l.LobbyID, PlayerID: ackB.PlayerID}).Marshal())
	var inv protocol.BattleInvite
	recv(t, b, protocol.MsgBattleInvite, &inv)
	if inv.LobbyID != l.LobbyID || inv.PlayerID != ackA.PlayerID || inv.Target != 3 {
		t.Fatalf("invite %+v", inv)
	}
	b.Send(protocol.MsgBattleJoin, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	if l2 := lobbyOf(t, b); len(l2.Members) != 2 {
		t.Fatalf("after join %+v", l2)
	}
	c.Send(protocol.MsgBattleJoin, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	if code := battleErr(t, c); code != protocol.BattleErrFull {
		t.Fatalf("third player should get full, got %d", code)
	}
	a.Send(protocol.MsgBattleStart, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	if code := battleErr(t, a); code != protocol.BattleErrNotReady {
		t.Fatalf("want not ready, got %d", code)
	}
	a.Send(protocol.MsgBattleReady, (&protocol.BattleReady{LobbyID: l.LobbyID, Ready: 1}).Marshal())
	b.Send(protocol.MsgBattleReady, (&protocol.BattleReady{LobbyID: l.LobbyID, Ready: 1}).Marshal())
	time.Sleep(100 * time.Millisecond)
	a.Send(protocol.MsgBattleStart, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	var sa, sb protocol.BattleStart
	recv(t, a, protocol.MsgBattleStart, &sa)
	recv(t, b, protocol.MsgBattleStart, &sb)
	if sa.Seed != sb.Seed || len(sa.Order) != 2 || sa.Order[0] != ackA.PlayerID {
		t.Fatalf("start %+v / %+v", sa, sb)
	}
	// Turn 1: both act -> both receive both actions.
	a.Send(protocol.MsgBattleAction, (&protocol.BattleAction{LobbyID: l.LobbyID, Turn: 1, Data: []byte{1}}).Marshal())
	b.Send(protocol.MsgBattleAction, (&protocol.BattleAction{LobbyID: l.LobbyID, Turn: 1, Data: []byte{2}}).Marshal())
	for _, cl := range []*mockclient.Client{a, b} {
		var turn protocol.BattleTurn
		recv(t, cl, protocol.MsgBattleTurn, &turn)
		if turn.Turn != 1 || len(turn.Actions) != 2 || turn.Actions[0].Data[0] != 1 || turn.Actions[1].Data[0] != 2 {
			t.Fatalf("turn %+v", turn)
		}
	}
	// Only the host reports the result.
	b.Send(protocol.MsgBattleResult, (&protocol.BattleResult{LobbyID: l.LobbyID, Outcome: protocol.OutcomeWin}).Marshal())
	if code := battleErr(t, b); code != protocol.BattleErrNotHost {
		t.Fatalf("want not host, got %d", code)
	}
	a.Send(protocol.MsgBattleResult, (&protocol.BattleResult{LobbyID: l.LobbyID, Outcome: protocol.OutcomeWin}).Marshal())
	for _, cl := range []*mockclient.Client{a, b} {
		var end protocol.BattleEnd
		recv(t, cl, protocol.MsgBattleEnd, &end)
		if end.Outcome != protocol.OutcomeWin || len(end.Players) != 2 {
			t.Fatalf("end %+v", end)
		}
		var pr protocol.Progress
		for {
			recv(t, cl, protocol.MsgProgress, &pr)
			if len(pr.Entries) > 0 {
				break
			}
		}
		if pr.Entries[0] != (protocol.ProgressEntry{Kind: protocol.BattleGym, Target: 3}) {
			t.Fatalf("progress %+v", pr)
		}
	}
}

func TestBattleTurnTimeoutAndDrop(t *testing.T) {
	addr := startServer(t, Config{TurnTimeout: 500 * time.Millisecond})
	a, _ := joinID(t, addr, "T", "Ash", 1)
	b, ackB := joinID(t, addr, "T", "Brock", 2)
	c, _ := joinID(t, addr, "T", "Misty", 3)
	// Elite Four is duo-only too.
	a.Send(protocol.MsgBattleCreate, (&protocol.BattleCreate{Kind: protocol.BattleEliteFour, Target: 1, MaxPlayers: 3}).Marshal())
	if code := battleErr(t, a); code != protocol.BattleErrBadRequest {
		t.Fatalf("3-player Elite Four should be refused, got %d", code)
	}
	a.Send(protocol.MsgBattleCreate, (&protocol.BattleCreate{Kind: protocol.BattleChampion, Target: 1, MaxPlayers: 3}).Marshal())
	l := lobbyOf(t, a)
	for _, cl := range []*mockclient.Client{b, c} {
		cl.Send(protocol.MsgBattleJoin, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	}
	time.Sleep(100 * time.Millisecond)
	for _, cl := range []*mockclient.Client{a, b, c} {
		cl.Send(protocol.MsgBattleReady, (&protocol.BattleReady{LobbyID: l.LobbyID, Ready: 1}).Marshal())
	}
	time.Sleep(100 * time.Millisecond)
	a.Send(protocol.MsgBattleStart, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	var st protocol.BattleStart
	recv(t, a, protocol.MsgBattleStart, &st)
	// Only A acts: after the deadline the turn goes out with empty actions for B and C.
	a.Send(protocol.MsgBattleAction, (&protocol.BattleAction{LobbyID: l.LobbyID, Turn: 1, Data: []byte{9}}).Marshal())
	var turn protocol.BattleTurn
	recv(t, a, protocol.MsgBattleTurn, &turn)
	if len(turn.Actions) != 3 {
		t.Fatalf("turn %+v", turn)
	}
	for _, x := range turn.Actions {
		if (len(x.Data) > 0) != (x.PlayerID == 1) {
			t.Fatalf("only Ash should have data: %+v", turn)
		}
	}
	// B drops mid-battle; A and C finish turn 2 without waiting for B.
	b.Close()
	a.Send(protocol.MsgBattleAction, (&protocol.BattleAction{LobbyID: l.LobbyID, Turn: 2, Data: []byte{1}}).Marshal())
	c.Send(protocol.MsgBattleAction, (&protocol.BattleAction{LobbyID: l.LobbyID, Turn: 2, Data: []byte{3}}).Marshal())
	deadline := time.Now().Add(300 * time.Millisecond) // well before the 500 ms timeout
	recv(t, a, protocol.MsgBattleTurn, &turn)
	if time.Now().After(deadline) || turn.Turn != 2 || len(turn.Actions) != 2 {
		t.Fatalf("turn 2 should complete without B: %+v", turn)
	}
	for _, x := range turn.Actions {
		if x.PlayerID == ackB.PlayerID {
			t.Fatal("dropped player still in turn")
		}
	}
}

func TestHelperWithBadgeCanJoin(t *testing.T) {
	addr := startServer(t, Config{})
	vet, ackVet := joinID(t, addr, "H", "Cynthia", 10)
	newb, ackNew := joinID(t, addr, "H", "Lucas", 11)

	solo := func(c *mockclient.Client) protocol.BattleLobby {
		c.Send(protocol.MsgBattleCreate, (&protocol.BattleCreate{Kind: protocol.BattleGym, Target: 1, MaxPlayers: 1}).Marshal())
		l := lobbyOf(t, c)
		c.Send(protocol.MsgBattleReady, (&protocol.BattleReady{LobbyID: l.LobbyID, Ready: 1}).Marshal())
		lobbyOf(t, c)
		c.Send(protocol.MsgBattleStart, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
		var st protocol.BattleStart
		recv(t, c, protocol.MsgBattleStart, &st)
		c.Send(protocol.MsgBattleResult, (&protocol.BattleResult{LobbyID: l.LobbyID, Outcome: protocol.OutcomeWin}).Marshal())
		var end protocol.BattleEnd
		recv(t, c, protocol.MsgBattleEnd, &end)
		return l
	}
	solo(vet) // Cynthia already has gym 1

	// Lucas hosts gym 1 and Cynthia joins to help.
	newb.Send(protocol.MsgBattleCreate, (&protocol.BattleCreate{Kind: protocol.BattleGym, Target: 1, MaxPlayers: 2}).Marshal())
	l := lobbyOf(t, newb)
	vet.Send(protocol.MsgBattleJoin, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	var lb protocol.BattleLobby
	for len(lb.Members) < 2 {
		lb = lobbyOf(t, newb)
	}
	for _, m := range lb.Members {
		want := uint8(0)
		if m.PlayerID == ackVet.PlayerID {
			want = 1
		}
		if m.Completed != want {
			t.Fatalf("completed flag wrong: %+v", lb.Members)
		}
	}
	for _, c := range []*mockclient.Client{newb, vet} {
		c.Send(protocol.MsgBattleReady, (&protocol.BattleReady{LobbyID: l.LobbyID, Ready: 1}).Marshal())
	}
	time.Sleep(100 * time.Millisecond)
	newb.Send(protocol.MsgBattleStart, (&protocol.LobbyRef{LobbyID: l.LobbyID}).Marshal())
	var st protocol.BattleStart
	recv(t, newb, protocol.MsgBattleStart, &st)
	newb.Send(protocol.MsgBattleResult, (&protocol.BattleResult{LobbyID: l.LobbyID, Outcome: protocol.OutcomeWin}).Marshal())
	var pr protocol.Progress
	for len(pr.Entries) == 0 {
		recv(t, newb, protocol.MsgProgress, &pr)
	}
	if pr.Entries[0] != (protocol.ProgressEntry{Kind: protocol.BattleGym, Target: 1}) {
		t.Fatalf("Lucas should now have gym 1: %+v", pr)
	}
	_ = ackNew
}
