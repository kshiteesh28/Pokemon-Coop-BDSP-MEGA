package protocol

import (
	"bytes"
	"reflect"
	"testing"
)

func TestSizesMatchGuide(t *testing.T) {
	cases := map[string]struct {
		got, want int
	}{
		"HELLO_REQ":    {len((&HelloReq{}).Marshal()), 52},
		"HELLO_REQ+pw": {len((&HelloReq{Password: "x"}).Marshal()), 68},
		"HELLO_ACK":    {len((&HelloAck{}).Marshal()), 8},
		"PLAYER_META":  {len((&PlayerMeta{}).Marshal()), 38},
		"ZONE_CHANGE":  {len((&ZoneChange{}).Marshal()), 24},
		"PLAYER_STATE": {len((&PlayerState{}).Marshal()), 28},
		"SNAPSHOT x32": {len((&WorldSnapshot{Entries: make([]SnapshotEntry, 32)}).Marshal()), 9 + 30*32},
		"PLAYER_LEAVE": {len((&PlayerLeave{}).Marshal()), 3},
		"PING":         {len((&PingPong{}).Marshal()), 4},
	}
	for name, c := range cases {
		if c.got != c.want {
			t.Errorf("%s: %d bytes, want %d", name, c.got, c.want)
		}
	}
}

func TestRoundTrip(t *testing.T) {
	h := HelloReq{RoomCode: "SINNOH-01", Appearance: Appearance{Name: "Lucas", FashionID: 3, ColorID: 2, Gender: 1, FollowSpecies: 448, FollowForm: 1, FollowShiny: 1}, InitialZone: 421, Password: "pw"}
	var h2 HelloReq
	if err := h2.Unmarshal(h.Marshal()); err != nil || !reflect.DeepEqual(h, h2) {
		t.Fatalf("hello %+v %v", h2, err)
	}
	s := WorldSnapshot{ServerTimeMs: 9, Zone: -1, Entries: []SnapshotEntry{{7, PlayerState{Zone: -1, X: 1, Y: 2, Z: 3, Yaw: 4, MoveSpeed: 5, AnimID: 6, FormState: 3, Flags: 5}}}}
	var s2 WorldSnapshot
	if err := s2.Unmarshal(s.Marshal()); err != nil || !reflect.DeepEqual(s, s2) {
		t.Fatalf("snap %+v %v", s2, err)
	}
}

func TestFraming(t *testing.T) {
	pkt := Encode(MsgPing, 42, (&PingPong{TimestampMs: 7}).Marshal())
	if !bytes.Equal(pkt[:8], []byte{0x43, 0x42, 1, 0x08, 4, 0, 42, 0}) {
		t.Fatalf("header % x", pkt[:8])
	}
	buf := make([]byte, MaxPayload)
	h, p, err := ReadPacket(bytes.NewReader(pkt), buf)
	if err != nil || h.Type != MsgPing || h.Seq != 42 || len(p) != 4 {
		t.Fatalf("%+v %v", h, err)
	}
	big := Encode(MsgCoopEvent, 0, nil)
	big[4], big[5] = 0x01, 0x10 // 4097
	if _, _, err := ReadPacket(bytes.NewReader(big), buf); err != ErrTooLarge {
		t.Fatalf("want ErrTooLarge, got %v", err)
	}
}

func TestStringsSanitizedAndTruncated(t *testing.T) {
	m := HelloReq{Appearance: Appearance{Name: "Bad\x07Name" + string(make([]byte, 0)) + "ñññññññññññññññññññññññ"}}
	var m2 HelloReq
	m2.Unmarshal(m.Marshal())
	if len(m2.Name) > 24 || bytes.ContainsRune([]byte(m2.Name), 7) {
		t.Fatalf("name %q", m2.Name)
	}
}
