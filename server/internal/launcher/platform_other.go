//go:build !windows

package launcher

import (
	"os"
	"os/exec"
	"runtime"
	"strconv"
)

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
