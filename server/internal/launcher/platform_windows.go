//go:build windows

package launcher

import (
	"encoding/base64"
	"fmt"
	"os/exec"
	"runtime"
	"strings"
	"syscall"
	"unicode/utf16"
)

const firewallSupported = true

// firewallRuleNames are the two rules host-server.bat creates; the launcher reuses them.
func firewallRuleNames(port int) []string {
	return []string{fmt.Sprintf("Coop server %d", port), fmt.Sprintf("Coop server %d (Tailscale)", port)}
}

func firewallOK(port int) bool {
	for _, name := range firewallRuleNames(port) {
		cmd := exec.Command("netsh", "advfirewall", "firewall", "show", "rule", "name="+name)
		hideWindow(cmd)
		if cmd.Run() != nil {
			return false
		}
	}
	return true
}

// addFirewallRules shows one Windows "Yes/No" prompt and adds both rules.
func addFirewallRules(port int) error {
	names := firewallRuleNames(port)
	script := fmt.Sprintf(
		"New-NetFirewallRule -DisplayName '%s' -Direction Inbound -Protocol TCP -LocalPort %d -Profile Private -Action Allow | Out-Null; "+
			"New-NetFirewallRule -DisplayName '%s' -Direction Inbound -Protocol TCP -LocalPort %d -RemoteAddress 100.64.0.0/10 -Action Allow | Out-Null",
		names[0], port, names[1], port)
	inner := base64.StdEncoding.EncodeToString(utf16le(script))
	outer := fmt.Sprintf("Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile -EncodedCommand %s'", inner)
	cmd := exec.Command("powershell", "-NoProfile", "-Command", outer)
	hideWindow(cmd)
	if err := cmd.Run(); err != nil {
		return fmt.Errorf("Windows didn't allow it (did you click No?)")
	}
	if !firewallOK(port) {
		return fmt.Errorf("the rules weren't added")
	}
	return nil
}

func utf16le(s string) []byte {
	u := utf16.Encode([]rune(s))
	b := make([]byte, 2*len(u))
	for i, c := range u {
		b[2*i], b[2*i+1] = byte(c), byte(c>>8)
	}
	return b
}

// keepAwake stops Windows from sleeping until the returned func is called.
func keepAwake() (stop func()) {
	proc := syscall.NewLazyDLL("kernel32.dll").NewProc("SetThreadExecutionState")
	if proc.Find() != nil {
		return func() {}
	}
	done := make(chan struct{})
	go func() {
		runtime.LockOSThread() // the flag belongs to this OS thread
		defer runtime.UnlockOSThread()
		const esContinuous, esSystemRequired = 0x80000000, 0x00000001
		proc.Call(esContinuous | esSystemRequired)
		<-done
		proc.Call(esContinuous)
	}()
	return func() { close(done) }
}

func openBrowser(url string) error {
	cmd := exec.Command("rundll32", "url.dll,FileProtocolHandler", url)
	return cmd.Start()
}

// hideWindow stops a console window flashing up for helper commands. It uses
// CREATE_NO_WINDOW rather than HideWindow: HideWindow also hides the first real
// window the program opens, which made the file picker invisible.
func hideWindow(cmd *exec.Cmd) {
	const createNoWindow = 0x08000000
	cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: createNoWindow}
}

// pickFile shows the Windows "Open" dialog in front of everything and returns
// the chosen path ("" if cancelled).
func pickFile(title, filter string) (string, error) {
	q := func(v string) string { return strings.ReplaceAll(v, "'", "''") }
	script := "Add-Type -AssemblyName System.Windows.Forms; " +
		"[System.Windows.Forms.Application]::EnableVisualStyles(); " +
		// An invisible, always-on-top owner window makes the dialog open in front.
		"$owner = New-Object System.Windows.Forms.Form -Property @{ TopMost = $true; ShowInTaskbar = $false; " +
		"StartPosition = 'CenterScreen'; Width = 1; Height = 1; Opacity = 0; FormBorderStyle = 'None' }; " +
		"$owner.Show(); $owner.Activate(); " +
		"$d = New-Object System.Windows.Forms.OpenFileDialog; " +
		"$d.Title = '" + q(title) + "'; $d.Filter = '" + q(filter) + "'; $d.CheckFileExists = $true; " +
		"$r = $d.ShowDialog($owner); $owner.Close(); " +
		"if ($r -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }"
	cmd := exec.Command("powershell", "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-Command", script)
	hideWindow(cmd)
	out, err := cmd.Output()
	if err != nil {
		return "", fmt.Errorf("The file picker didn't open (%v). Use \"Paste path\" instead.", err)
	}
	return strings.TrimSpace(string(out)), nil
}

// runningRyujinx returns the path of a running Ryujinx.exe, if any.
func runningRyujinx() string {
	cmd := exec.Command("powershell", "-NoProfile", "-Command", "(Get-Process Ryujinx -ErrorAction SilentlyContinue | Select-Object -First 1).Path")
	hideWindow(cmd)
	out, _ := cmd.Output()
	return strings.TrimSpace(string(out))
}

func openFolder(path string) error { return exec.Command("explorer", path).Start() }
