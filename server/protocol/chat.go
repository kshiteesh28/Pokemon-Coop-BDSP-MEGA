package protocol

// Room chat (0x40). Client → server: text. Server → every player in the room
// (including the sender, so everyone sees the same order): sender id, name, text.
const MsgChat MsgType = 0x40

// MaxChatBytes caps one chat message (UTF-8).
const MaxChatBytes = 200

type ChatSend struct{ Text string }

func (m *ChatSend) Marshal() []byte { return []byte(truncUTF8(m.Text, MaxChatBytes)) }
func (m *ChatSend) Unmarshal(b []byte) error {
	m.Text = sanitize(string(b[:min(len(b), MaxChatBytes)]))
	return nil
}

type ChatMessage struct {
	PlayerID uint16
	Name     string
	Text     string
}

func (m *ChatMessage) Marshal() []byte {
	w := &writer{}
	w.u16(m.PlayerID)
	w.str(m.Name, 24)
	w.b = append(w.b, truncUTF8(m.Text, MaxChatBytes)...)
	return w.b
}
func (m *ChatMessage) Unmarshal(b []byte) error {
	r := &reader{b: b}
	m.PlayerID = r.u16()
	m.Name = r.str(24)
	m.Text = sanitize(string(r.rest()))
	return r.err
}
