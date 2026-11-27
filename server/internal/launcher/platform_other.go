//go:build !windows

package launcher

import (
	"errors"
	"os"
	"os/exec"
	"runtime"
	"strconv"
	"strings"
)

var errNoPicker = errors.New("file picker not available on this system; type the path instead")

// macOS and Linux don't block incoming connections by default.
const firewallSupported = false

func firewallOK(port int) bool            { return true }
func addFirewallRules(port int) error     { return nil }
func firewallRuleNames(port int) []string { return nil }

// keepAwake uses macOS's caffeinate while the launcher runs; elsewhere it does nothing.
func keepAwake() (stop func()) {
	if runtime.GOOS != "darwin" {
		return func() {}
	}
	cmd := exec.Command("caffeinate", "-i", "-w", strconv.Itoa(os.Getpid()))
	if cmd.Start() != nil {
		return func() {}
	}
	return func() { cmd.Process.Kill(); cmd.Wait() }
}

func openBrowser(url string) error {
	if runtime.GOOS == "darwin" {
		return exec.Command("open", url).Start()
	}
	return exec.Command("xdg-open", url).Start()
}

func hideWindow(cmd *exec.Cmd) {}

// pickFile uses the macOS file chooser; elsewhere it isn't available.
func pickFile(title, filter string) (string, error) {
	if runtime.GOOS != "darwin" {
		return "", errNoPicker
	}
	script := `POSIX path of (choose file with prompt "` + strings.ReplaceAll(title, `"`, `'`) + `")`
	out, err := exec.Command("osascript", "-e", script).Output()
	if err != nil {
		return "", nil // cancelled
	}
	return strings.TrimSpace(string(out)), nil
}

func runningRyujinx() string { return "" }

func openFolder(path string) error {
	if runtime.GOOS == "darwin" {
		return exec.Command("open", path).Start()
	}
	return exec.Command("xdg-open", path).Start()
}
