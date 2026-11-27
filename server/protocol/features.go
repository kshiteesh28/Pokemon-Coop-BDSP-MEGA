package protocol

// Legendary registry (0x20–0x24) and battle lobbies (0x30–0x3C).
// These are server extensions beyond the original guide; see server/README.md.

const (
	MsgLegendReserve    MsgType = 0x20 // C→S  species u16
	MsgLegendReserveAck MsgType = 0x21 // S→C  species u16, status u8, holder_name[24]
	MsgLegendResult     MsgType = 0x22 // C→S  species u16, result u8
	MsgLegendUpdate     MsgType = 0x23 // S→C  species u16, state u8, owner_name[24]
	MsgLegendTransfer   MsgType = 0x24 // C→S  species u16, to_player_id u16

	MsgBattleCreate MsgType = 0x30 // C→S  kind u8, target u16, max_players u8
	MsgBattleLobby  MsgType = 0x31 // S→C  lobby state (members carry ready + completed flags)
	MsgBattleInvite MsgType = 0x32 // both lobby_id u16, player_id u16, kind u8, target u16
	MsgBattleJoin   MsgType = 0x33 // C→S  lobby_id u16
	MsgBattleLeave  MsgType = 0x34 // C→S  lobby_id u16
	MsgBattleReady  MsgType = 0x35 // C→S  lobby_id u16, ready u8
	MsgBattleStart  MsgType = 0x36 // C→S lobby_id u16 (host) / S→C lobby_id, seed u64, order
	MsgBattleAction MsgType = 0x37 // C→S  lobby_id u16, turn u16, data[...]
	MsgBattleTurn   MsgType = 0x38 // S→C  lobby_id u16, turn u16, count u8, count×(player_id u16, len u16, data)
	MsgBattleResult MsgType = 0x39 // C→S  lobby_id u16, outcome u8 (host only)
	MsgBattleEnd    MsgType = 0x3A // S→C  lobby_id u16, outcome u8, count u8, count×player_id u16
	MsgBattleError  MsgType = 0x3B // S→C  lobby_id u16, code u8
	MsgProgress     MsgType = 0x3C // S→C  count u8, count×(kind u8, target u16)
)

// LEGEND_RESERVE_ACK status.
const (
	LegendGranted       uint8 = 0 // go ahead with the encounter
	LegendOwnedByOther  uint8 = 1 // someone already caught it: block the encounter
	LegendReservedOther uint8 = 2 // someone is battling it right now: block the encounter
	LegendUntracked     uint8 = 3 // species isn't on the server's legendary list: go ahead
	LegendYouOwn        uint8 = 4 // you already own it
)

// LEGEND_RESULT result.
const (
	LegendCaught   uint8 = 0
	LegendEscaped  uint8 = 1 // fled, fainted it, or lost: it becomes available again
	LegendReleased uint8 = 2 // owner released it from their box
)

// LEGEND_UPDATE state.
const (
	LegendFree     uint8 = 0
	LegendReserved uint8 = 1
	LegendOwned    uint8 = 2
)

// Battle kinds. Wins of kinds 0–2 are recorded as achievements for the whole team.
const (
	BattleGym       uint8 = 0 // target = gym number 1–8
	BattleEliteFour uint8 = 1 // target = Elite Four member 1–4
	BattleChampion  uint8 = 2 // target = 1
	BattleFree      uint8 = 3 // anything else; no credit recorded
)

// Battle lobby states.
const (
	LobbyOpen     uint8 = 0
	LobbyInBattle uint8 = 1
)

// Battle outcomes.
const (
	OutcomeWin  uint8 = 0
	OutcomeLoss uint8 = 1
)

// BATTLE_ERROR codes.
const (
	BattleErrNoLobby    uint8 = 1
	BattleErrFull       uint8 = 2
	BattleErrOtherRoom  uint8 = 3
	BattleErrInLobby    uint8 = 4
	BattleErrNotHost    uint8 = 5
	BattleErrNotReady   uint8 = 6
	BattleErrStarted    uint8 = 7
	BattleErrBadTurn    uint8 = 8
	BattleErrNotMember  uint8 = 9
	BattleErrBadRequest uint8 = 10
)

// MaxBattleActionData bounds one player's per-turn action blob.
const MaxBattleActionData = 512

// MaxBattlePlayers is the largest lobby the server allows (Champion, free battles).
const MaxBattlePlayers = 4

// MaxDuoPlayers: gym and Elite Four battles are solo or duo only.
const MaxDuoPlayers = 2

type LegendReserve struct{ Species uint16 }

func (m *LegendReserve) Marshal() []byte { w := &writer{}; w.u16(m.Species); return w.b }
func (m *LegendReserve) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.Species = r.u16()
	return r.err
}

type LegendReserveAck struct {
	Species    uint16
	Status     uint8
	HolderName string
}

func (m *LegendReserveAck) Marshal() []byte {
	w := &writer{}
	w.u16(m.Species)
	w.u8(m.Status)
	w.str(m.HolderName, 24)
	return w.b
}
func (m *LegendReserveAck) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.Species, m.Status, m.HolderName = r.u16(), r.u8(), r.str(24)
	return r.err
}

type LegendResult struct {
	Species uint16
	Result  uint8
}

func (m *LegendResult) Marshal() []byte { w := &writer{}; w.u16(m.Species); w.u8(m.Result); return w.b }
func (m *LegendResult) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.Species, m.Result = r.u16(), r.u8()
	return r.err
}

type LegendUpdate struct {
	Species   uint16
	State     uint8
	OwnerName string
}

func (m *LegendUpdate) Marshal() []byte {
	w := &writer{}
	w.u16(m.Species)
	w.u8(m.State)
	w.str(m.OwnerName, 24)
	return w.b
}
func (m *LegendUpdate) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.Species, m.State, m.OwnerName = r.u16(), r.u8(), r.str(24)
	return r.err
}

type LegendTransfer struct {
	Species  uint16
	ToPlayer uint16
}

func (m *LegendTransfer) Marshal() []byte {
	w := &writer{}
	w.u16(m.Species)
	w.u16(m.ToPlayer)
	return w.b
}
func (m *LegendTransfer) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.Species, m.ToPlayer = r.u16(), r.u16()
	return r.err
}

type BattleCreate struct {
	Kind       uint8
	Target     uint16
	MaxPlayers uint8
}

func (m *BattleCreate) Marshal() []byte {
	w := &writer{}
	w.u8(m.Kind)
	w.u16(m.Target)
	w.u8(m.MaxPlayers)
	return w.b
}
func (m *BattleCreate) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.Kind, m.Target, m.MaxPlayers = r.u8(), r.u16(), r.u8()
	return r.err
}

type BattleMember struct {
	PlayerID  uint16
	Ready     uint8
	Completed uint8 // 1 = already beat this gym/E4 member/Champion: joining as a helper
}

type BattleLobby struct {
	LobbyID    uint16
	Kind       uint8
	Target     uint16
	Host       uint16
	State      uint8
	MaxPlayers uint8
	Members    []BattleMember
}

func (m *BattleLobby) Marshal() []byte {
	w := &writer{}
	w.u16(m.LobbyID)
	w.u8(m.Kind)
	w.u16(m.Target)
	w.u16(m.Host)
	w.u8(m.State)
	w.u8(m.MaxPlayers)
	w.u8(uint8(len(m.Members)))
	for _, x := range m.Members {
		w.u16(x.PlayerID)
		w.u8(x.Ready)
		w.u8(x.Completed)
	}
	return w.b
}
func (m *BattleLobby) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.Kind, m.Target, m.Host, m.State, m.MaxPlayers = r.u16(), r.u8(), r.u16(), r.u16(), r.u8(), r.u8()
	n := int(r.u8())
	m.Members = nil
	for i := 0; i < n && r.err == nil; i++ {
		m.Members = append(m.Members, BattleMember{r.u16(), r.u8(), r.u8()})
	}
	return r.err
}

// BattleInvite: C→S PlayerID is who to invite; S→C PlayerID is who invited you.
type BattleInvite struct {
	LobbyID  uint16
	PlayerID uint16
	Kind     uint8
	Target   uint16
}

func (m *BattleInvite) Marshal() []byte {
	w := &writer{}
	w.u16(m.LobbyID)
	w.u16(m.PlayerID)
	w.u8(m.Kind)
	w.u16(m.Target)
	return w.b
}
func (m *BattleInvite) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.PlayerID = r.u16(), r.u16()
	if len(b) >= 7 {
		m.Kind, m.Target = r.u8(), r.u16()
	}
	return r.err
}

// LobbyRef is the payload of BATTLE_JOIN, BATTLE_LEAVE and the C→S BATTLE_START.
type LobbyRef struct{ LobbyID uint16 }

func (m *LobbyRef) Marshal() []byte { w := &writer{}; w.u16(m.LobbyID); return w.b }
func (m *LobbyRef) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID = r.u16()
	return r.err
}

type BattleReady struct {
	LobbyID uint16
	Ready   uint8
}

func (m *BattleReady) Marshal() []byte { w := &writer{}; w.u16(m.LobbyID); w.u8(m.Ready); return w.b }
func (m *BattleReady) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.Ready = r.u16(), r.u8()
	return r.err
}

// BattleStart (S→C): every client seeds its battle RNG with Seed and assigns
// battle slots in Order, so all games simulate the identical battle.
type BattleStart struct {
	LobbyID uint16
	Seed    uint64
	Order   []uint16
}

func (m *BattleStart) Marshal() []byte {
	w := &writer{}
	w.u16(m.LobbyID)
	w.u32(uint32(m.Seed))
	w.u32(uint32(m.Seed >> 32))
	w.u8(uint8(len(m.Order)))
	for _, id := range m.Order {
		w.u16(id)
	}
	return w.b
}
func (m *BattleStart) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID = r.u16()
	lo, hi := r.u32(), r.u32()
	m.Seed = uint64(lo) | uint64(hi)<<32
	n := int(r.u8())
	m.Order = nil
	for i := 0; i < n && r.err == nil; i++ {
		m.Order = append(m.Order, r.u16())
	}
	return r.err
}

type BattleAction struct {
	LobbyID uint16
	Turn    uint16
	Data    []byte
}

func (m *BattleAction) Marshal() []byte {
	w := &writer{}
	w.u16(m.LobbyID)
	w.u16(m.Turn)
	w.b = append(w.b, m.Data...)
	return w.b
}
func (m *BattleAction) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.Turn = r.u16(), r.u16()
	m.Data = r.rest()
	return r.err
}

type TurnAction struct {
	PlayerID uint16
	Data     []byte // empty = player timed out or dropped; client picks a default action
}

type BattleTurn struct {
	LobbyID uint16
	Turn    uint16
	Actions []TurnAction
}

func (m *BattleTurn) Marshal() []byte {
	w := &writer{}
	w.u16(m.LobbyID)
	w.u16(m.Turn)
	w.u8(uint8(len(m.Actions)))
	for _, a := range m.Actions {
		w.u16(a.PlayerID)
		w.u16(uint16(len(a.Data)))
		w.b = append(w.b, a.Data...)
	}
	return w.b
}
func (m *BattleTurn) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.Turn = r.u16(), r.u16()
	n := int(r.u8())
	m.Actions = nil
	for i := 0; i < n && r.err == nil; i++ {
		id, l := r.u16(), int(r.u16())
		if !r.need(l) {
			break
		}
		m.Actions = append(m.Actions, TurnAction{id, append([]byte(nil), r.b[r.off:r.off+l]...)})
		r.off += l
	}
	return r.err
}

type BattleResult struct {
	LobbyID uint16
	Outcome uint8
}

func (m *BattleResult) Marshal() []byte {
	w := &writer{}
	w.u16(m.LobbyID)
	w.u8(m.Outcome)
	return w.b
}
func (m *BattleResult) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.Outcome = r.u16(), r.u8()
	return r.err
}

type BattleEnd struct {
	LobbyID uint16
	Outcome uint8
	Players []uint16 // members present at the end (they get credit on a win)
}

func (m *BattleEnd) Marshal() []byte {
	w := &writer{}
	w.u16(m.LobbyID)
	w.u8(m.Outcome)
	w.u8(uint8(len(m.Players)))
	for _, id := range m.Players {
		w.u16(id)
	}
	return w.b
}
func (m *BattleEnd) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.Outcome = r.u16(), r.u8()
	n := int(r.u8())
	m.Players = nil
	for i := 0; i < n && r.err == nil; i++ {
		m.Players = append(m.Players, r.u16())
	}
	return r.err
}

type BattleError struct {
	LobbyID uint16
	Code    uint8
}

func (m *BattleError) Marshal() []byte { w := &writer{}; w.u16(m.LobbyID); w.u8(m.Code); return w.b }
func (m *BattleError) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.LobbyID, m.Code = r.u16(), r.u8()
	return r.err
}

type ProgressEntry struct {
	Kind   uint8
	Target uint16
}

type Progress struct{ Entries []ProgressEntry }

func (m *Progress) Marshal() []byte {
	w := &writer{}
	w.u8(uint8(len(m.Entries)))
	for _, e := range m.Entries {
		w.u8(e.Kind)
		w.u16(e.Target)
	}
	return w.b
}
func (m *Progress) Unmarshal(b []byte) error {
	r := &reader{b: b}
	n := int(r.u8())
	m.Entries = nil
	for i := 0; i < n && r.err == nil; i++ {
		m.Entries = append(m.Entries, ProgressEntry{r.u8(), r.u16()})
	}
	return r.err
}
