package launcher

import (
	"bytes"
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/config"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

func freePort(t *testing.T) int {
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer ln.Close()
	return ln.Addr().(*net.TCPAddr).Port
}

type harness struct {
	t   *testing.T
	app *App
	srv *httptest.Server
	dir string
}

func newHarness(t *testing.T) *harness {
	dir := t.TempDir()
	app, err := New(Options{ConfigPath: filepath.Join(dir, "coop.ini"), DataPath: filepath.Join(dir, "data.json"), HostPort: freePort(t)})
	if err != nil {
		t.Fatal(err)
	}
	srv := httptest.NewServer(app.Handler())
	t.Cleanup(func() { app.Shutdown(); srv.Close() })
	return &harness{t, app, srv, dir}
}

func (h *harness) call(path string, body any) (int, map[string]any) {
	h.t.Helper()
	method, rdr := http.MethodGet, &bytes.Buffer{}
	if body != nil {
		method = http.MethodPost
		json.NewEncoder(rdr).Encode(body)
	}
	req, _ := http.NewRequest(method, h.srv.URL+path, rdr)
	req.Header.Set("X-Coop-Token", h.app.Token())
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		h.t.Fatal(err)
	}
	defer resp.Body.Close()
	var out map[string]any
	json.NewDecoder(resp.Body).Decode(&out)
	return resp.StatusCode, out
}

func (h *harness) state() stateView {
	h.t.Helper()
	req, _ := http.NewRequest(http.MethodGet, h.srv.URL+"/api/state", nil)
	req.Header.Set("X-Coop-Token", h.app.Token())
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		h.t.Fatal(err)
	}
	defer resp.Body.Close()
	var st stateView
	if err := json.NewDecoder(resp.Body).Decode(&st); err != nil {
		h.t.Fatal(err)
	}
	return st
}

func waitFor(t *testing.T, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(50 * time.Millisecond)
	}
	t.Fatalf("timed out waiting for %s", what)
}

func TestSecurity(t *testing.T) {
	h := newHarness(t)
	for _, tc := range []struct {
		name, url, token, host string
		want                   int
	}{
		{"page without token", "/", "", "", 403},
		{"page with token", "/?t=" + h.app.Token(), "", "", 200},
		{"api without token", "/api/state", "", "", 403},
		{"api wrong token", "/api/state", "nope", "", 403},
		{"dns rebinding host", "/api/state", h.app.Token(), "evil.example:80", 403},
		{"api ok", "/api/state", h.app.Token(), "", 200},
	} {
		req, _ := http.NewRequest(http.MethodGet, h.srv.URL+tc.url, nil)
		if tc.token != "" {
			req.Header.Set("X-Coop-Token", tc.token)
		}
		if tc.host != "" {
			req.Host = tc.host
		}
		resp, err := http.DefaultClient.Do(req)
		if err != nil {
			t.Fatal(err)
		}
		resp.Body.Close()
		if resp.StatusCode != tc.want {
			t.Errorf("%s: got %d, want %d", tc.name, resp.StatusCode, tc.want)
		}
	}
	// State-changing calls must be POST.
	req, _ := http.NewRequest(http.MethodGet, h.srv.URL+"/api/host", nil)
	req.Header.Set("X-Coop-Token", h.app.Token())
	resp, _ := http.DefaultClient.Do(req)
	resp.Body.Close()
	if resp.StatusCode != http.StatusMethodNotAllowed {
		t.Errorf("GET /api/host: %d", resp.StatusCode)
	}
}

func TestSettingsValidationAndSave(t *testing.T) {
	h := newHarness(t)
	if code, out := h.call("/api/settings", map[string]string{"Name": "", "Room": "x"}); code != 400 || !strings.Contains(out["error"].(string), "name") {
		t.Fatalf("empty name: %d %v", code, out)
	}
	if code, _ := h.call("/api/settings", map[string]string{"Name": "Ash", "Room": "bad room!"}); code != 400 {
		t.Fatalf("bad room accepted")
	}
	if code, out := h.call("/api/settings", map[string]string{"Name": " Aaroh ", "Room": "aaroh", "Address": "100.67.109.85", "Password": "pw"}); code != 200 {
		t.Fatalf("save: %d %v", code, out)
	}
	c, ok, err := config.LoadClient(filepath.Join(h.dir, "coop.ini"))
	if !ok || err != nil || c.Name != "Aaroh" || c.Room != "AAROH" || c.Address != "100.67.109.85:4100" || c.Password != "pw" {
		t.Fatalf("saved %+v", c)
	}
	// Game paths set elsewhere survive a settings save.
	h.app.setGamePaths("game", `D:\BD.nsp`)
	h.call("/api/settings", map[string]string{"Name": "Aaroh", "Room": "AAROH"})
	if c, _, _ := config.LoadClient(filepath.Join(h.dir, "coop.ini")); c.Game != `D:\BD.nsp` {
		t.Fatalf("game path lost: %+v", c)
	}
}

func TestHostJoinChat(t *testing.T) {
	h := newHarness(t)
	h.call("/api/settings", map[string]string{"Name": "Aaroh", "Room": "AAROH", "Password": "pw"})

	if code, out := h.call("/api/join", map[string]bool{"on": true}); code != 400 || !strings.Contains(out["error"].(string), "address") {
		t.Fatalf("join with no address and no host should explain: %d %v", code, out)
	}
	if code, out := h.call("/api/host", map[string]bool{"on": true}); code != 200 {
		t.Fatalf("host: %d %v", code, out)
	}
	// A second server on the same port fails with a clear message.
	h2, _ := New(Options{ConfigPath: filepath.Join(h.dir, "b.ini"), HostPort: h.app.opt.HostPort})
	if err := h2.startHost(); err == nil || !strings.Contains(err.Error(), "already in use") {
		t.Fatalf("port clash: %v", err)
	}

	// Joining your own server needs no address.
	if code, out := h.call("/api/join", map[string]bool{"on": true}); code != 200 {
		t.Fatalf("join: %d %v", code, out)
	}
	// A friend joins too.
	friend, err := JoinServer(config.Client{Address: "127.0.0.1:" + itoa(h.app.opt.HostPort), Room: "aaroh", Name: "Rahul", Password: "pw"}, func(string, ...any) {})
	if err != nil {
		t.Fatal(err)
	}
	defer friend.Leave()
	_, err = JoinServer(config.Client{Address: "127.0.0.1:" + itoa(h.app.opt.HostPort), Room: "aaroh", Name: "Bad", Password: "wrong"}, func(string, ...any) {})
	if err == nil || !strings.Contains(err.Error(), "Wrong password") {
		t.Fatalf("wrong password: %v", err)
	}

	waitFor(t, "host status shows 2 players and the app sees Rahul", func() bool {
		st := h.state()
		if !st.Host.Running || st.Host.Status == nil || len(st.Host.Status.Rooms) != 1 || len(st.Host.Status.Rooms[0].Players) != 2 {
			return false
		}
		for _, p := range st.Join.Peers {
			if p.Name == "Rahul" {
				return true
			}
		}
		return false
	})

	// Chat goes both ways.
	if code, out := h.call("/api/chat", map[string]string{"text": "hello from the app"}); code != 200 {
		t.Fatalf("chat: %d %v", code, out)
	}
	friend.Say("hi back")
	waitFor(t, "both chat lines", func() bool {
		c := h.state().Join.Chat
		return len(c) == 2 && c[0].Mine && c[0].Text == "hello from the app" && c[1].From == "Rahul" && c[1].Text == "hi back"
	})
	waitFor(t, "friend sees app message", func() bool {
		for _, l := range friend.View().Chat {
			if l.From == "Aaroh" && l.Text == "hello from the app" {
				return true
			}
		}
		return false
	})

	// Stopping the server kicks everyone and the app shows it.
	h.call("/api/host", map[string]bool{"on": false})
	waitFor(t, "friend sees disconnect", func() bool { return friend.View().State == "offline" })
	if st := h.state(); st.Host.Running {
		t.Fatal("still hosting")
	}
}

func TestGameChecklist(t *testing.T) {
	dir := t.TempDir()
	ryu := filepath.Join(dir, "Ryujinx", "Ryujinx.exe")
	data := filepath.Join(dir, "Ryujinx", "portable") // portable mode keeps the test self-contained
	game := filepath.Join(dir, "games", "Pokemon Brilliant Diamond [0100000011D90000].nsp")
	mk := func(p string) { os.MkdirAll(filepath.Dir(p), 0o755); os.WriteFile(p, []byte("x"), 0o644) }
	mk(ryu)
	os.MkdirAll(data, 0o755)

	c := config.Client{Ryujinx: ryu}
	v := gameView(c)
	if v.Ready || v.DataDir != data {
		t.Fatalf("empty setup shouldn't be ready: %+v", v)
	}
	mk(filepath.Join(data, "system", "prod.keys"))
	mk(filepath.Join(data, "bis", "system", "Contents", "registered", "a.nca", "00"))
	mods := filepath.Join(data, "mods", "contents", BDSPTitleID)
	mk(filepath.Join(mods, "Luminescent Platinum.zip"))
	if v := gameView(c); checkOK(v, "lumi") || !strings.Contains(checkDetail(v, "lumi"), ".zip") {
		t.Fatalf("zip-only should point at extracting: %+v", v.Checks)
	}
	mk(filepath.Join(mods, "Luminescent Platinum", "exefs", "main.npdm"))
	mk(filepath.Join(mods, "Luminescent Platinum", "romfs", "x"))

	// Auto-detect finds the game through Ryujinx's Config.json game folders.
	cfgJSON, _ := json.Marshal(map[string]any{"game_dirs": []string{filepath.Dir(game)}})
	os.WriteFile(filepath.Join(data, "Config.json"), cfgJSON, 0o644)
	mk(game)
	mk(filepath.Join(filepath.Dir(game), "Pokemon Brilliant Diamond Update 1.3.0.nsp"))
	if got := findGame(data); got != game {
		t.Fatalf("findGame = %q", got)
	}
	c.Game = game
	v = gameView(c)
	if !v.Ready || checkOK(v, "mega") {
		t.Fatalf("should be ready without mega: %+v", v.Checks)
	}
	mk(filepath.Join(mods, "Luminescent Platinum", "exefs", "subsdk2"))
	if v := gameView(c); !checkOK(v, "mega") {
		t.Fatalf("mega not detected: %+v", v.Checks)
	}
	if v := gameView(c); checkOK(v, "coop") || !v.Ready || v.CoopAge != -1 {
		t.Fatalf("co-op client is optional and absent: %+v", v)
	}
	mk(filepath.Join(mods, "Luminescent Platinum", "exefs", "subsdk3"))
	os.MkdirAll(filepath.Join(data, "sdcard", "config"), 0o755)
	os.WriteFile(filepath.Join(data, "sdcard", "config", "pokemon-coop-status.txt"), []byte("state: connected\nmap: 421\n"), 0o644)
	if v := gameView(c); !checkOK(v, "coop") || !strings.HasPrefix(v.CoopStatus, "state: connected") || v.CoopAge < 0 || v.CoopAge > 5 {
		t.Fatalf("co-op client/status not detected: %+v", v)
	}

	// The game's co-op settings land on the virtual SD card.
	p, err := writeGameCoopConfig(data, config.Client{Address: "1.2.3.4:4100", Room: "AAROH", Name: "Aaroh", Ryujinx: ryu})
	if err != nil || p != filepath.Join(data, "sdcard", "config", "pokemon-coop.ini") {
		t.Fatal(p, err)
	}
	got, _, _ := config.LoadClient(p)
	if got.Address != "1.2.3.4:4100" || got.Ryujinx != "" {
		t.Fatalf("game config %+v", got)
	}
}

func checkOK(v GameView, id string) bool {
	for _, c := range v.Checks {
		if c.ID == id {
			return c.OK
		}
	}
	return false
}

func checkDetail(v GameView, id string) string {
	for _, c := range v.Checks {
		if c.ID == id {
			return c.Detail
		}
	}
	return ""
}

func TestParseTailscaleStatus(t *testing.T) {
	js := `{"BackendState":"Running","Self":{"HostName":"mystogan","TailscaleIPs":["100.67.109.85","fd7a::1"]},
	"Peer":{"k1":{"HostName":"rahul-mac","OS":"macOS","TailscaleIPs":["100.80.1.2"],"Online":true},"k2":{"DNSName":"ravi-pc.tail.ts.net.","TailscaleIPs":["100.80.1.3"]}}}`
	v, err := parseTailscaleStatus([]byte(js))
	if err != nil || v.State != "running" || v.IP != "100.67.109.85" || v.Device != "mystogan" || len(v.Peers) != 2 {
		t.Fatalf("%+v %v", v, err)
	}
	if v, _ := parseTailscaleStatus([]byte(`{"BackendState":"NeedsLogin"}`)); v.State != "needs_login" {
		t.Fatalf("%+v", v)
	}
}

func itoa(n int) string { b, _ := json.Marshal(n); return string(b) }

func TestPastePath(t *testing.T) {
	h := newHarness(t)
	exe := filepath.Join(h.dir, "Ryujinx.exe")
	os.WriteFile(exe, []byte("x"), 0o644)
	if code, out := h.call("/api/game/path", map[string]string{"what": "ryujinx", "path": filepath.Join(h.dir, "nope.exe")}); code != 400 || !strings.Contains(out["error"].(string), "Couldn't find") {
		t.Fatalf("missing file accepted: %d %v", code, out)
	}
	// Quotes from Explorer's "Copy as path" are stripped.
	if code, out := h.call("/api/game/path", map[string]string{"what": "ryujinx", "path": `"` + exe + `"`}); code != 200 {
		t.Fatalf("paste: %d %v", code, out)
	}
	if got := h.state().Settings.Ryujinx; got != exe {
		t.Fatalf("ryujinx = %q", got)
	}
}

func TestBattleLobbyFromApp(t *testing.T) {
	h := newHarness(t)
	h.call("/api/settings", map[string]string{"Name": "Aaroh", "Room": "AAROH"})
	h.call("/api/host", map[string]bool{"on": true})
	if code, out := h.call("/api/join", map[string]bool{"on": true}); code != 200 {
		t.Fatalf("join: %d %v", code, out)
	}
	friend, err := JoinServer(config.Client{Address: "127.0.0.1:" + itoa(h.app.opt.HostPort), Room: "AAROH", Name: "Rahul"}, func(string, ...any) {})
	if err != nil {
		t.Fatal(err)
	}
	defer friend.Leave()

	// The roster lists both, even across maps.
	waitFor(t, "roster", func() bool { return len(h.state().Join.Roster) == 2 })

	// 3-player gym is refused with a friendly message.
	h.call("/api/battle", map[string]any{"action": "create", "kind": 0, "target": 1, "max": 3})
	waitFor(t, "duo-only note", func() bool { return strings.Contains(h.state().Join.BattleNote.Text, "solo or duo") })

	h.call("/api/battle", map[string]any{"action": "create", "kind": 0, "target": 1, "max": 2})
	waitFor(t, "lobby", func() bool { l := h.state().Join.Lobby; return l != nil && l.YouAreHost })
	var rahulID uint16
	for _, r := range h.state().Join.Roster {
		if r.Name == "Rahul" {
			rahulID = r.ID
		}
	}
	h.call("/api/battle", map[string]any{"action": "invite", "player": rahulID})
	waitFor(t, "friend gets invite", func() bool {
		v := friend.View()
		return len(v.Invites) == 1 && v.Invites[0].From == "Aaroh" && v.Invites[0].Target == 1
	})
	inv := friend.View().Invites[0]
	if err := friend.BattleAction("join", 0, 0, 0, 0, inv.LobbyID, 0); err != nil {
		t.Fatal(err)
	}
	waitFor(t, "two members", func() bool { l := h.state().Join.Lobby; return l != nil && len(l.Members) == 2 })
	h.call("/api/battle", map[string]any{"action": "ready"})
	friend.BattleAction("ready", 0, 0, 0, 0, 0, 0)
	waitFor(t, "both ready", func() bool {
		l := h.state().Join.Lobby
		return l != nil && len(l.Members) == 2 && l.Members[0].Ready && l.Members[1].Ready
	})
	h.call("/api/battle", map[string]any{"action": "start"})
	waitFor(t, "in battle", func() bool { l := h.state().Join.Lobby; return l != nil && l.InBattle })
	h.call("/api/battle", map[string]any{"action": "result", "outcome": 0})
	waitFor(t, "badge for both", func() bool {
		st := h.state().Join
		fv := friend.View()
		has := func(p []protocol.ProgressEntry) bool {
			return len(p) == 1 && p[0] == protocol.ProgressEntry{Kind: protocol.BattleGym, Target: 1}
		}
		return st.Lobby == nil && has(st.Progress) && has(fv.Progress) && strings.Contains(st.BattleNote.Text, "won")
	})
}
