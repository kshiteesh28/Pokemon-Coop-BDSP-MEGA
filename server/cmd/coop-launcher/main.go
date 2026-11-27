// Pokemon Co-op: the desktop app. Host or join a server, chat, check
// Tailscale and start the game, all from one window.
//
// The window is the app's own page shown by Edge/Chrome in "app mode" (no
// tabs or address bar). Closing the window quits the app and stops hosting.
package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/exec"
	"os/signal"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/launcher"
)

const version = "0.1.0"

func main() {
	uiPort := flag.Int("ui-port", 4180, "local port for the app window")
	hostPort := flag.Int("port", 4100, "port your server listens on when hosting")
	noWindow := flag.Bool("no-window", false, "don't open the app window (print the link instead)")
	flag.Parse()

	base := appDir()
	stateDir := filepath.Join(userConfigDir(), "PokemonCoop")
	os.MkdirAll(stateDir, 0o755)
	if f, err := os.OpenFile(filepath.Join(stateDir, "launcher.log"), os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o644); err == nil {
		log.SetOutput(io.MultiWriter(os.Stderr, f))
		defer f.Close()
	}

	// Already running? Bring up its window instead of starting a second copy.
	instFile := filepath.Join(stateDir, "instance.json")
	if url := runningInstance(instFile); url != "" {
		log.Printf("already running, opening its window")
		openWindow(url, stateDir)
		return
	}

	app, err := launcher.New(launcher.Options{
		ConfigPath: filepath.Join(base, "coop.ini"),
		DataPath:   filepath.Join(base, "coop-data.json"),
		HostPort:   *hostPort,
		Version:    version,
	})
	if err != nil {
		fatal(err)
	}

	ln, err := net.Listen("tcp", fmt.Sprintf("127.0.0.1:%d", *uiPort))
	if err != nil {
		ln, err = net.Listen("tcp", "127.0.0.1:0")
		if err != nil {
			fatal(err)
		}
	}
	url := fmt.Sprintf("http://127.0.0.1:%d/?t=%s", ln.Addr().(*net.TCPAddr).Port, app.Token())
	b, _ := json.Marshal(map[string]string{"url": url, "token": app.Token()})
	os.WriteFile(instFile, b, 0o600)
	defer os.Remove(instFile)

	srv := &http.Server{Handler: app.Handler(), ReadHeaderTimeout: 5 * time.Second}
	go srv.Serve(ln)
	log.Printf("Pokemon Co-op %s: %s (settings in %s)", version, url, base)

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	windowClosed := make(chan struct{})
	if *noWindow {
		fmt.Println("Open this link:", url)
	} else {
		go func() {
			openWindow(url, stateDir)
			close(windowClosed)
		}()
	}

	// Quit when the window closes, the user clicks Quit, Ctrl+C, or (for a
	// plain browser tab we can't watch) nobody has looked for 3 minutes.
	idle := time.NewTicker(10 * time.Second)
	defer idle.Stop()
loop:
	for {
		select {
		case <-ctx.Done():
			break loop
		case <-app.Quit:
			break loop
		case <-windowClosed:
			log.Printf("window closed")
			break loop
		case <-idle.C:
			if !*noWindow && app.SecondsSincePoll() > 180 {
				log.Printf("no window for 3 minutes, quitting")
				break loop
			}
		}
	}
	app.Shutdown()
	shutdownCtx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	srv.Shutdown(shutdownCtx)
	cancel()
}

// appDir is where coop.ini and coop-data.json live: next to the program, or
// the current folder when run with `go run`.
func appDir() string {
	exe, err := os.Executable()
	if err != nil || strings.Contains(exe, "go-build") {
		wd, _ := os.Getwd()
		return wd
	}
	return filepath.Dir(exe)
}

func userConfigDir() string {
	if d, err := os.UserConfigDir(); err == nil {
		return d
	}
	return os.TempDir()
}

func runningInstance(path string) string {
	b, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	var inst struct{ URL, Token string }
	if json.Unmarshal(b, &inst) != nil || inst.URL == "" {
		return ""
	}
	base := strings.SplitN(inst.URL, "/?", 2)[0]
	req, _ := http.NewRequest(http.MethodGet, base+"/api/state", nil)
	req.Header.Set("X-Coop-Token", inst.Token)
	c := http.Client{Timeout: 2 * time.Second}
	resp, err := c.Do(req)
	if err != nil {
		return ""
	}
	resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return ""
	}
	return inst.URL
}

// openWindow shows url in an app-mode window and blocks until it's closed.
// If no Edge/Chrome is found it opens the default browser and returns at once
// with a channel that never closes (the idle timer handles quitting then).
func openWindow(url, stateDir string) {
	browser := findAppBrowser()
	if browser == "" {
		openDefault(url)
		select {}
	}
	profile := filepath.Join(stateDir, "window")
	cmd := exec.Command(browser, "--app="+url, "--user-data-dir="+profile, "--window-size=1320,880",
		"--no-first-run", "--no-default-browser-check", "--disable-features=Translate")
	start := time.Now()
	if err := cmd.Run(); err != nil && time.Since(start) < 3*time.Second {
		log.Printf("app window failed (%v), using default browser", err)
		openDefault(url)
		select {}
	}
	if time.Since(start) < 5*time.Second {
		// The browser handed the window to an already-open copy and exited;
		// we can't watch that window, so rely on the idle timer instead.
		select {}
	}
}

func findAppBrowser() string {
	var cands []string
	switch runtime.GOOS {
	case "windows":
		for _, env := range []string{"ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA"} {
			d := os.Getenv(env)
			if d == "" {
				continue
			}
			cands = append(cands, filepath.Join(d, "Microsoft", "Edge", "Application", "msedge.exe"),
				filepath.Join(d, "Google", "Chrome", "Application", "chrome.exe"))
		}
	case "darwin":
		cands = []string{"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
			"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
			"/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"}
	default:
		for _, n := range []string{"google-chrome", "chromium", "chromium-browser", "microsoft-edge"} {
			if p, err := exec.LookPath(n); err == nil {
				cands = append(cands, p)
			}
		}
	}
	for _, c := range cands {
		if st, err := os.Stat(c); err == nil && !st.IsDir() {
			return c
		}
	}
	return ""
}

func openDefault(url string) {
	switch runtime.GOOS {
	case "windows":
		exec.Command("rundll32", "url.dll,FileProtocolHandler", url).Start()
	case "darwin":
		exec.Command("open", url).Start()
	default:
		exec.Command("xdg-open", url).Start()
	}
}

func fatal(err error) {
	log.Printf("error: %v", err)
	if runtime.GOOS == "windows" {
		msg := strings.ReplaceAll(err.Error(), "'", "''")
		exec.Command("powershell", "-NoProfile", "-Command",
			"Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show('"+msg+"', 'Pokemon Co-op')").Run()
	}
	os.Exit(1)
}
