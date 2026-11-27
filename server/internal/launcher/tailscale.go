package launcher

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
	"time"
)

// TailscaleView is what the app shows about the Tailscale install.
type TailscaleView struct {
	Installed bool     `json:"installed"`
	State     string   `json:"state"` // "running", "needs_login", "stopped", "not_installed", "unknown"
	IP        string   `json:"ip"`
	Device    string   `json:"device"`
	Peers     []TSPeer `json:"peers"`
}

type TSPeer struct {
	Name   string `json:"name"`
	IP     string `json:"ip"`
	Online bool   `json:"online"`
	OS     string `json:"os"`
}

// tailscaleCLI finds the tailscale command-line tool.
func tailscaleCLI() string {
	if p, err := exec.LookPath("tailscale"); err == nil {
		return p
	}
	var cands []string
	switch runtime.GOOS {
	case "windows":
		for _, env := range []string{"ProgramFiles", "ProgramFiles(x86)"} {
			if d := os.Getenv(env); d != "" {
				cands = append(cands, filepath.Join(d, "Tailscale", "tailscale.exe"))
			}
		}
	case "darwin":
		cands = append(cands, "/Applications/Tailscale.app/Contents/MacOS/Tailscale", "/usr/local/bin/tailscale", "/opt/homebrew/bin/tailscale")
	}
	for _, c := range cands {
		if _, err := os.Stat(c); err == nil {
			return c
		}
	}
	return ""
}

// tsStatusJSON is the subset of `tailscale status --json` we use.
type tsStatusJSON struct {
	BackendState string
	Self         *tsNode
	Peer         map[string]*tsNode
}

type tsNode struct {
	HostName     string
	DNSName      string
	OS           string
	TailscaleIPs []string
	Online       bool
}

func parseTailscaleStatus(b []byte) (TailscaleView, error) {
	var s tsStatusJSON
	if err := json.Unmarshal(b, &s); err != nil {
		return TailscaleView{}, err
	}
	v := TailscaleView{Installed: true, Peers: []TSPeer{}}
	switch s.BackendState {
	case "Running":
		v.State = "running"
	case "NeedsLogin", "NeedsMachineAuth":
		v.State = "needs_login"
	case "Stopped":
		v.State = "stopped"
	default:
		v.State = "unknown"
	}
	ipv4 := func(n *tsNode) string {
		for _, ip := range n.TailscaleIPs {
			if strings.Count(ip, ".") == 3 {
				return ip
			}
		}
		return ""
	}
	if s.Self != nil {
		v.IP, v.Device = ipv4(s.Self), s.Self.HostName
	}
	for _, p := range s.Peer {
		name := p.HostName
		if name == "" {
			name = strings.Split(p.DNSName, ".")[0]
		}
		v.Peers = append(v.Peers, TSPeer{Name: name, IP: ipv4(p), Online: p.Online, OS: p.OS})
	}
	return v, nil
}

func tailscaleStatus() TailscaleView {
	cli := tailscaleCLI()
	if cli == "" {
		return TailscaleView{State: "not_installed", Peers: []TSPeer{}}
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, cli, "status", "--json")
	hideWindow(cmd)
	out, err := cmd.Output()
	if len(out) == 0 && err != nil {
		return TailscaleView{Installed: true, State: "stopped", Peers: []TSPeer{}}
	}
	v, err := parseTailscaleStatus(out)
	if err != nil {
		return TailscaleView{Installed: true, State: "unknown", Peers: []TSPeer{}}
	}
	return v
}

var loginURL = regexp.MustCompile(`https://login\.tailscale\.com/\S+`)

// tailscaleConnect runs `tailscale up`. If Tailscale needs you to sign in it
// prints a link, which is opened in your browser.
func tailscaleConnect(logf func(string, ...any)) error {
	cli := tailscaleCLI()
	if cli == "" {
		return fmt.Errorf("Tailscale isn't installed yet. Click Install, then try again.")
	}
	cmd := exec.Command(cli, "up")
	hideWindow(cmd)
	stderr, _ := cmd.StderrPipe()
	stdout, _ := cmd.StdoutPipe()
	if err := cmd.Start(); err != nil {
		return fmt.Errorf("Couldn't start Tailscale: %v", err)
	}
	found := make(chan string, 1)
	scan := func(r interface{ Read([]byte) (int, error) }) {
		sc := bufio.NewScanner(r)
		for sc.Scan() {
			if u := loginURL.FindString(sc.Text()); u != "" {
				select {
				case found <- u:
				default:
				}
			}
		}
	}
	go scan(stderr)
	go scan(stdout)
	exited := make(chan error, 1)
	go func() { exited <- cmd.Wait() }()
	select {
	case u := <-found:
		logf("Opening Tailscale sign-in in your browser")
		openBrowser(u)
		return nil // `tailscale up` keeps waiting for the sign-in and exits by itself
	case err := <-exited:
		if err != nil {
			return fmt.Errorf("Tailscale couldn't connect. Open the Tailscale app once and sign in there.")
		}
		logf("Tailscale connected")
		return nil
	case <-time.After(20 * time.Second):
		return fmt.Errorf("Tailscale is taking a while. Check the Tailscale icon in your system tray.")
	}
}
