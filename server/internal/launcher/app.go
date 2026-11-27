// Package launcher is the "Pokemon Co-op" desktop app: a small local web
// server whose page is shown in its own app window. From one place you can
// host a server, join one, chat, and see players, legendaries and badges.
package launcher

import (
	"context"
	"crypto/rand"
	_ "embed"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"log"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/config"
	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/coop"
)

//go:embed ui/index.html
var indexHTML []byte

type Options struct {
	ConfigPath string // coop.ini
	DataPath   string // coop-data.json (legendaries, badges) when hosting
	HostPort   int    // default 4100
	Version    string
}

type App struct {
	opt   Options
	token string
	Quit  chan struct{}

	mu       sync.Mutex
	cfg      config.Client
	host     *hosting
	hostErr  string
	player   *Player
	joinErr  string
	logs     []LogLine
	logSeq   int
	lastPoll time.Time
	quitOnce sync.Once
}

type hosting struct {
	srv     *coop.Server
	cancel  context.CancelFunc
	done    chan struct{}
	started time.Time
	awake   func()
	port    int
}

type LogLine struct {
	Seq  int    `json:"seq"`
	Time string `json:"time"`
	Text string `json:"text"`
}

func New(opt Options) (*App, error) {
	if opt.HostPort == 0 {
		opt.HostPort = 4100
	}
	if opt.ConfigPath == "" {
		opt.ConfigPath = "coop.ini"
	}
	if opt.DataPath == "" {
		opt.DataPath = "coop-data.json"
	}
	cfg, _, err := config.LoadClient(opt.ConfigPath)
	if err != nil {
		return nil, err
	}
	var tok [16]byte
	rand.Read(tok[:])
	a := &App{opt: opt, token: hex.EncodeToString(tok[:]), Quit: make(chan struct{}), cfg: cfg, lastPoll: time.Now()}
	a.logf("Co-op app started")
	return a, nil
}

// Token is required on every API call so other websites can't control the app.
func (a *App) Token() string { return a.token }

func (a *App) logf(format string, args ...any) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.addLog(fmt.Sprintf(format, args...))
}

func (a *App) addLog(text string) {
	a.logSeq++
	a.logs = append(a.logs, LogLine{Seq: a.logSeq, Time: time.Now().Format("15:04:05"), Text: text})
	if len(a.logs) > 200 {
		a.logs = a.logs[len(a.logs)-200:]
	}
}

// logWriter feeds the server's log lines into the activity panel.
type logWriter struct{ a *App }

func (w logWriter) Write(p []byte) (int, error) {
	for _, line := range strings.Split(strings.TrimRight(string(p), "\n"), "\n") {
		if line = strings.TrimSpace(line); line != "" {
			w.a.logf("%s", line)
		}
	}
	return len(p), nil
}

// ---------------------------------------------------------------- actions

func (a *App) startHost() error {
	a.mu.Lock()
	if a.host != nil {
		a.mu.Unlock()
		return nil
	}
	cfg, port := a.cfg, a.opt.HostPort
	a.mu.Unlock()

	ln, err := net.Listen("tcp", fmt.Sprintf(":%d", port))
	if err != nil {
		return fmt.Errorf("Port %d is already in use. Is another server (like host-server.bat) already running?", port)
	}
	srv, err := coop.New(coop.Config{Password: cfg.Password, DataPath: a.opt.DataPath, Logger: log.New(logWriter{a}, "", 0)})
	if err != nil {
		ln.Close()
		return fmt.Errorf("Couldn't load %s: %v", a.opt.DataPath, err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	h := &hosting{srv: srv, cancel: cancel, done: make(chan struct{}), started: time.Now(), awake: keepAwake(), port: port}
	go func() {
		srv.Serve(ctx, ln)
		close(h.done)
	}()
	a.mu.Lock()
	a.host, a.hostErr = h, ""
	a.mu.Unlock()
	pw := "no password"
	if cfg.Password != "" {
		pw = "password on"
	}
	a.logf("Server started on port %d (%s)", port, pw)
	return nil
}

func (a *App) stopHost() {
	a.mu.Lock()
	h := a.host
	a.host = nil
	a.mu.Unlock()
	if h == nil {
		return
	}
	h.cancel()
	<-h.done
	h.awake()
	a.logf("Server stopped")
}

func (a *App) startJoin() error {
	a.mu.Lock()
	if a.player != nil {
		a.mu.Unlock()
		return nil
	}
	cfg := a.cfg
	if cfg.Address == "" && a.host != nil {
		cfg.Address = fmt.Sprintf("127.0.0.1:%d", a.host.port)
	}
	a.mu.Unlock()
	p, err := JoinServer(cfg, a.logf)
	a.mu.Lock()
	defer a.mu.Unlock()
	if err != nil {
		a.joinErr = err.Error()
		return err
	}
	a.player, a.joinErr = p, ""
	return nil
}

func (a *App) stopJoin() {
	a.mu.Lock()
	p := a.player
	a.player = nil
	a.mu.Unlock()
	if p != nil {
		p.Leave()
	}
}

// Shutdown stops everything; safe to call more than once.
func (a *App) Shutdown() {
	a.stopJoin()
	a.stopHost()
	a.quitOnce.Do(func() { close(a.Quit) })
}

func validateSettings(c config.Client) (config.Client, error) {
	c.Name = strings.TrimSpace(c.Name)
	c.Address = strings.TrimSpace(c.Address)
	c.Password = strings.TrimSpace(c.Password)
	if c.Name == "" {
		return c, fmt.Errorf("Enter a trainer name.")
	}
	if len(c.Name) > 24 {
		return c, fmt.Errorf("Trainer names can be at most 24 characters.")
	}
	room, ok := coop.NormalizeRoomCode(c.Room)
	if !ok || len(room) > 16 {
		return c, fmt.Errorf("Room codes use letters, numbers, - and _ (up to 16).")
	}
	c.Room = room
	if len(c.Password) > 16 {
		return c, fmt.Errorf("Passwords can be at most 16 characters.")
	}
	if c.Address != "" {
		if _, _, err := net.SplitHostPort(c.Address); err != nil {
			c.Address = net.JoinHostPort(strings.Trim(c.Address, "[]"), "4100")
		}
	}
	return c, nil
}

// ---------------------------------------------------------------- HTTP

type hostView struct {
	Running     bool         `json:"running"`
	Port        int          `json:"port"`
	SinceSec    int64        `json:"since_sec"`
	Error       string       `json:"error,omitempty"`
	Addrs       []Addr       `json:"addrs"`
	Firewall    firewallView `json:"firewall"`
	Status      *coop.Status `json:"status,omitempty"`
	PasswordSet bool         `json:"password_set"`
}

type firewallView struct {
	Supported bool `json:"supported"`
	OK        bool `json:"ok"`
}

type stateView struct {
	Version   string        `json:"version"`
	OS        string        `json:"os"`
	Settings  config.Client `json:"settings"`
	Host      hostView      `json:"host"`
	Join      PlayerView    `json:"join"`
	Tailscale TailscaleView `json:"tailscale"`
	Game      GameView      `json:"game"`
	Logs      []LogLine     `json:"logs"`
}

func (a *App) state() stateView {
	a.mu.Lock()
	a.lastPoll = time.Now()
	st := stateView{Version: a.opt.Version, OS: runtime.GOOS, Settings: a.cfg, Logs: append([]LogLine{}, a.logs...)}
	cfg := a.cfg
	h, p, joinErr := a.host, a.player, a.joinErr
	st.Host = hostView{Port: a.opt.HostPort, Error: a.hostErr, PasswordSet: a.cfg.Password != ""}
	a.mu.Unlock()

	st.Host.Addrs = DetectAddrs()
	if st.Host.Addrs == nil {
		st.Host.Addrs = []Addr{}
	}
	st.Host.Firewall = firewallView{Supported: firewallSupported, OK: true}
	st.Tailscale = cachedTailscale()
	st.Game = gameView(cfg)
	if h != nil {
		st.Host.Running = true
		st.Host.SinceSec = int64(time.Since(h.started).Seconds())
		st.Host.Firewall.OK = cachedFirewallOK(h.port)
		ctx, cancel := context.WithTimeout(context.Background(), 500*time.Millisecond)
		if s, err := h.srv.Status(ctx); err == nil {
			st.Host.Status = &s
		}
		cancel()
	}
	if p != nil {
		st.Join = p.View()
	} else {
		st.Join = PlayerView{State: "off", Reason: joinErr, Peers: []PeerView{}, Legends: []LegendView{}, Chat: []ChatLine{}}
	}
	return st
}

var ts struct {
	sync.Mutex
	v       TailscaleView
	checked time.Time
}

func cachedTailscale() TailscaleView {
	ts.Lock()
	defer ts.Unlock()
	if time.Since(ts.checked) > 4*time.Second {
		ts.v, ts.checked = tailscaleStatus(), time.Now()
	}
	return ts.v
}

func forgetTailscale() {
	ts.Lock()
	ts.checked = time.Time{}
	ts.Unlock()
}

var fw struct {
	sync.Mutex
	ok      bool
	checked time.Time
}

func cachedFirewallOK(port int) bool {
	fw.Lock()
	defer fw.Unlock()
	if time.Since(fw.checked) > 10*time.Second {
		fw.ok, fw.checked = firewallOK(port), time.Now()
	}
	return fw.ok
}

// Handler serves the UI and API. Only loopback Host headers and requests
// carrying the token are accepted.
func (a *App) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/" || r.URL.Query().Get("t") != a.token {
			http.Error(w, "Open the app from Pokemon Co-op, not directly.", http.StatusForbidden)
			return
		}
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		w.Header().Set("Content-Security-Policy", "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'")
		w.Write(indexHTML)
	})
	api := func(path string, fn func(r *http.Request) (any, error)) {
		mux.HandleFunc(path, func(w http.ResponseWriter, r *http.Request) {
			if r.Header.Get("X-Coop-Token") != a.token {
				http.Error(w, "forbidden", http.StatusForbidden)
				return
			}
			if path != "/api/state" && r.Method != http.MethodPost {
				http.Error(w, "POST only", http.StatusMethodNotAllowed)
				return
			}
			out, err := fn(r)
			w.Header().Set("Content-Type", "application/json")
			if err != nil {
				w.WriteHeader(http.StatusBadRequest)
				json.NewEncoder(w).Encode(map[string]string{"error": err.Error()})
				return
			}
			json.NewEncoder(w).Encode(out)
		})
	}
	ok := map[string]bool{"ok": true}

	api("/api/state", func(r *http.Request) (any, error) { return a.state(), nil })
	api("/api/settings", func(r *http.Request) (any, error) {
		var c config.Client
		if err := json.NewDecoder(http.MaxBytesReader(nil, r.Body, 4096)).Decode(&c); err != nil {
			return nil, fmt.Errorf("bad request")
		}
		c, err := validateSettings(c)
		if err != nil {
			return nil, err
		}
		a.mu.Lock()
		c.Ryujinx, c.Game = a.cfg.Ryujinx, a.cfg.Game
		a.mu.Unlock()
		if err := config.SaveClient(a.opt.ConfigPath, c); err != nil {
			return nil, fmt.Errorf("Couldn't save coop.ini: %v", err)
		}
		a.mu.Lock()
		c.Ryujinx, c.Game = a.cfg.Ryujinx, a.cfg.Game // set by the Play panel, not this form
		pwChanged := a.host != nil && a.cfg.Password != c.Password
		a.cfg = c
		a.mu.Unlock()
		a.logf("Settings saved")
		if pwChanged {
			a.logf("Restart the server for the new password to apply")
		}
		return ok, nil
	})
	api("/api/host", func(r *http.Request) (any, error) {
		var req struct{ On bool }
		json.NewDecoder(r.Body).Decode(&req)
		if !req.On {
			a.stopHost()
			return ok, nil
		}
		if err := a.startHost(); err != nil {
			a.mu.Lock()
			a.hostErr = err.Error()
			a.mu.Unlock()
			return nil, err
		}
		return ok, nil
	})
	api("/api/join", func(r *http.Request) (any, error) {
		var req struct{ On bool }
		json.NewDecoder(r.Body).Decode(&req)
		if !req.On {
			a.stopJoin()
			return ok, nil
		}
		return ok, a.startJoin()
	})
	api("/api/chat", func(r *http.Request) (any, error) {
		var req struct{ Text string }
		json.NewDecoder(http.MaxBytesReader(nil, r.Body, 4096)).Decode(&req)
		a.mu.Lock()
		p := a.player
		a.mu.Unlock()
		if p == nil {
			return nil, fmt.Errorf("Join a room to chat.")
		}
		return ok, p.Say(req.Text)
	})
	api("/api/firewall", func(r *http.Request) (any, error) {
		if err := addFirewallRules(a.opt.HostPort); err != nil {
			return nil, err
		}
		fw.Lock()
		fw.checked = time.Time{}
		fw.Unlock()
		a.logf("Firewall rules added")
		return ok, nil
	})
	api("/api/tailscale", func(r *http.Request) (any, error) {
		var req struct{ Action string }
		json.NewDecoder(r.Body).Decode(&req)
		defer forgetTailscale()
		switch req.Action {
		case "install":
			return ok, openBrowser("https://tailscale.com/download")
		case "share":
			return ok, openBrowser("https://login.tailscale.com/admin/machines")
		case "connect":
			return ok, tailscaleConnect(a.logf)
		}
		return nil, fmt.Errorf("unknown action")
	})
	api("/api/game/pick", func(r *http.Request) (any, error) {
		var req struct{ What string }
		json.NewDecoder(r.Body).Decode(&req)
		title, filter := "Choose Ryujinx.exe", "Ryujinx (Ryujinx.exe)|Ryujinx.exe|Programs (*.exe)|*.exe"
		if req.What == "game" {
			title, filter = "Choose your Pokemon Brilliant Diamond game file", "Switch games|*.nsp;*.xci|All files|*.*"
		}
		path, err := pickFile(title, filter)
		if err != nil {
			return nil, err
		}
		if path == "" {
			return ok, nil // cancelled
		}
		return ok, a.setGamePaths(req.What, path)
	})
	api("/api/game/path", func(r *http.Request) (any, error) {
		var req struct{ What, Path string }
		json.NewDecoder(http.MaxBytesReader(nil, r.Body, 8192)).Decode(&req)
		p := strings.Trim(strings.TrimSpace(req.Path), `"'`) // Explorer's "Copy as path" adds quotes
		if p == "" {
			return nil, fmt.Errorf("Paste a path first.")
		}
		if !isFile(p) && !(runtime.GOOS == "darwin" && strings.HasSuffix(p, ".app") && isDir(p)) {
			return nil, fmt.Errorf("Couldn't find a file at %s", p)
		}
		what := "ryujinx"
		if req.What == "game" {
			what = "game"
		}
		return ok, a.setGamePaths(what, p)
	})
	api("/api/game/detect", func(r *http.Request) (any, error) {
		a.mu.Lock()
		c := a.cfg
		a.mu.Unlock()
		found := 0
		if c.Ryujinx == "" || !isFile(c.Ryujinx) {
			if p := findRyujinx(); p != "" {
				a.setGamePaths("ryujinx", p)
				c.Ryujinx = p
				found++
			}
		}
		if c.Game == "" || !isFile(c.Game) {
			if p := findGame(ryujinxDataDir(c.Ryujinx)); p != "" {
				a.setGamePaths("game", p)
				found++
			}
		}
		return map[string]int{"found": found}, nil
	})
	api("/api/game/mods", func(r *http.Request) (any, error) {
		a.mu.Lock()
		dir := filepath.Join(ryujinxDataDir(a.cfg.Ryujinx), "mods", "contents", BDSPTitleID)
		a.mu.Unlock()
		os.MkdirAll(dir, 0o755)
		return ok, openFolder(dir)
	})
	api("/api/game/play", func(r *http.Request) (any, error) {
		a.mu.Lock()
		c := a.cfg
		if c.Address == "" && a.host != nil {
			c.Address = fmt.Sprintf("127.0.0.1:%d", a.host.port)
		}
		a.mu.Unlock()
		gv := gameView(c)
		if !gv.Ready {
			for _, ch := range gv.Checks {
				if !ch.OK && ch.ID != "mega" {
					return nil, fmt.Errorf("Not ready yet: %s. %s", ch.Label, ch.Detail)
				}
			}
		}
		if c.Name != "" && c.Address != "" {
			if p, err := writeGameCoopConfig(gv.DataDir, c); err == nil {
				a.logf("Co-op settings written for the game (%s)", p)
			}
		}
		if err := launchGame(c); err != nil {
			return nil, err
		}
		a.logf("Starting Brilliant Diamond in Ryujinx")
		return ok, nil
	})
	api("/api/quit", func(r *http.Request) (any, error) {
		go a.Shutdown()
		return ok, nil
	})

	return hostGuard(mux)
}

// hostGuard blocks DNS-rebinding: only localhost names may reach the app.
func hostGuard(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		host, _, err := net.SplitHostPort(r.Host)
		if err != nil {
			host = r.Host
		}
		if host != "127.0.0.1" && host != "localhost" && host != "::1" {
			http.Error(w, "forbidden", http.StatusForbidden)
			return
		}
		next.ServeHTTP(w, r)
	})
}

func (a *App) setGamePaths(what, path string) error {
	a.mu.Lock()
	c := a.cfg
	if what == "game" {
		c.Game = path
	} else {
		c.Ryujinx = path
	}
	a.cfg = c
	a.mu.Unlock()
	if err := config.SaveClient(a.opt.ConfigPath, c); err != nil {
		return fmt.Errorf("Couldn't save coop.ini: %v", err)
	}
	a.logf("Saved %s path: %s", what, path)
	return nil
}

// SecondsSincePoll reports how long ago the window last asked for state.
func (a *App) SecondsSincePoll() float64 {
	a.mu.Lock()
	defer a.mu.Unlock()
	return time.Since(a.lastPoll).Seconds()
}
