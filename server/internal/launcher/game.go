package launcher

import (
	"encoding/json"
	"fmt"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/config"
)

// BDSPTitleID is Brilliant Diamond's title ID; mods live under this folder.
const BDSPTitleID = "0100000011d90000"

// Check is one line of the "Ready to play?" checklist.
type Check struct {
	ID     string `json:"id"`
	Label  string `json:"label"`
	OK     bool   `json:"ok"`
	Detail string `json:"detail"`
}

type GameView struct {
	Ryujinx string   `json:"ryujinx"`
	Game    string   `json:"game"`
	DataDir string   `json:"data_dir"`
	Checks  []Check  `json:"checks"`
	Ready   bool     `json:"ready"`
	Mods    []string `json:"mods"`
	// What the in-game co-op client last reported (sd:/config/pokemon-coop-status.txt).
	CoopStatus string `json:"coop_status"`
	CoopAge    int    `json:"coop_age_sec"` // seconds since the game last updated it; -1 = never
}

// ryujinxDataDir is where Ryujinx keeps keys, mods and the SD card. A
// "portable" folder next to Ryujinx.exe takes priority, like Ryujinx does.
func ryujinxDataDir(ryujinxPath string) string {
	if ryujinxPath != "" {
		if p := filepath.Join(filepath.Dir(ryujinxPath), "portable"); isDir(p) {
			return p
		}
	}
	switch runtime.GOOS {
	case "windows":
		return filepath.Join(os.Getenv("APPDATA"), "Ryujinx")
	case "darwin":
		home, _ := os.UserHomeDir()
		return filepath.Join(home, "Library", "Application Support", "Ryujinx")
	default:
		home, _ := os.UserHomeDir()
		return filepath.Join(home, ".config", "Ryujinx")
	}
}

func isDir(p string) bool  { st, err := os.Stat(p); return err == nil && st.IsDir() }
func isFile(p string) bool { st, err := os.Stat(p); return err == nil && !st.IsDir() }

// findRyujinx guesses where Ryujinx is installed.
func findRyujinx() string {
	if p := runningRyujinx(); p != "" {
		return p
	}
	home, _ := os.UserHomeDir()
	var cands []string
	switch runtime.GOOS {
	case "windows":
		for _, d := range []string{`C:\Emulators\Ryujinx`, `C:\Ryujinx`, filepath.Join(home, "Desktop", "Ryujinx"),
			filepath.Join(home, "Downloads", "Ryujinx"), filepath.Join(home, "Documents", "Ryujinx"),
			filepath.Join(os.Getenv("LOCALAPPDATA"), "Ryujinx"), filepath.Join(os.Getenv("ProgramFiles"), "Ryujinx")} {
			cands = append(cands, filepath.Join(d, "Ryujinx.exe"), filepath.Join(d, "publish", "Ryujinx.exe"))
		}
	case "darwin":
		cands = []string{"/Applications/Ryujinx.app", filepath.Join(home, "Applications", "Ryujinx.app")}
	}
	for _, c := range cands {
		if isFile(c) || (runtime.GOOS == "darwin" && isDir(c)) {
			return c
		}
	}
	return ""
}

// findGame looks through Ryujinx's game folders for a Brilliant Diamond file.
func findGame(dataDir string) string {
	var cfg struct {
		GameDirs []string `json:"game_dirs"`
	}
	if b, err := os.ReadFile(filepath.Join(dataDir, "Config.json")); err == nil {
		json.Unmarshal(b, &cfg)
	}
	for _, dir := range cfg.GameDirs {
		var found string
		base := strings.Count(filepath.Clean(dir), string(filepath.Separator))
		filepath.WalkDir(dir, func(p string, d fs.DirEntry, err error) error {
			if err != nil || found != "" {
				return filepath.SkipDir
			}
			if d.IsDir() && strings.Count(p, string(filepath.Separator))-base > 2 {
				return filepath.SkipDir
			}
			if looksLikeBD(d.Name()) {
				found = p
				return filepath.SkipAll
			}
			return nil
		})
		if found != "" {
			return found
		}
	}
	return ""
}

func looksLikeBD(name string) bool {
	n := strings.ToLower(name)
	ext := filepath.Ext(n)
	if ext != ".nsp" && ext != ".xci" {
		return false
	}
	if strings.Contains(n, "update") || strings.Contains(n, "[upd]") || strings.Contains(n, "dlc") {
		return false
	}
	return strings.Contains(n, BDSPTitleID) || strings.Contains(n, "brilliant diamond") || strings.Contains(n, "brilliant_diamond")
}

// gameView builds the "Ready to play?" checklist.
func gameView(c config.Client) GameView {
	v := GameView{Ryujinx: c.Ryujinx, Game: c.Game, Mods: []string{}}
	data := ryujinxDataDir(c.Ryujinx)
	v.DataDir = data
	add := func(id, label string, ok bool, detail string) {
		v.Checks = append(v.Checks, Check{ID: id, Label: label, OK: ok, Detail: detail})
	}

	ryuOK := c.Ryujinx != "" && (isFile(c.Ryujinx) || (runtime.GOOS == "darwin" && isDir(c.Ryujinx)))
	add("ryujinx", "Ryujinx emulator", ryuOK, map[bool]string{true: filepath.Base(c.Ryujinx), false: "Choose where Ryujinx is"}[ryuOK])

	keys := isFile(filepath.Join(data, "system", "prod.keys"))
	add("keys", "Switch keys (prod.keys)", keys, map[bool]string{true: "Found", false: "Put prod.keys in Ryujinx's system folder (File > Open Ryujinx Folder)"}[keys])

	fwDir := filepath.Join(data, "bis", "system", "Contents", "registered")
	entries, _ := os.ReadDir(fwDir)
	add("firmware", "Switch firmware", len(entries) > 0, map[bool]string{true: "Installed", false: "In Ryujinx: Tools > Install Firmware"}[len(entries) > 0])

	gameOK := c.Game != "" && isFile(c.Game)
	add("game", "Brilliant Diamond game file", gameOK, map[bool]string{true: filepath.Base(c.Game), false: "Choose your Brilliant Diamond .nsp/.xci"}[gameOK])

	modDir := filepath.Join(data, "mods", "contents", BDSPTitleID)
	lumi, mega, coopMod, zipLeft := "", false, false, false
	if ents, err := os.ReadDir(modDir); err == nil {
		for _, e := range ents {
			if !e.IsDir() {
				if strings.HasSuffix(strings.ToLower(e.Name()), ".zip") {
					zipLeft = true
				}
				continue
			}
			v.Mods = append(v.Mods, e.Name())
			p := filepath.Join(modDir, e.Name())
			if lumi == "" && strings.Contains(strings.ToLower(e.Name()), "lumi") && isDir(filepath.Join(p, "exefs")) && isDir(filepath.Join(p, "romfs")) {
				lumi = e.Name()
			}
			if isFile(filepath.Join(p, "exefs", "subsdk2")) {
				mega = true
			}
			if isFile(filepath.Join(p, "exefs", "subsdk3")) {
				coopMod = true
			}
		}
	}
	lumiDetail := "Extract Luminescent Platinum into mods\\contents\\" + BDSPTitleID + "\\Luminescent Platinum (with exefs and romfs inside)"
	if lumi != "" {
		lumiDetail = lumi
	} else if zipLeft {
		lumiDetail = "Found a .zip there: extract it first (exefs and romfs must be folders)"
	}
	add("lumi", "Luminescent Platinum mod", lumi != "", lumiDetail)
	add("mega", "Mega Evolution mod (subsdk2)", mega, map[bool]string{true: "Found", false: "Copy MegaExtension's exefs\\subsdk2 into a mod's exefs folder"}[mega])
	add("coop", "Co-op client (subsdk3)", coopMod, map[bool]string{true: "Found: the game will connect to your room", false: "Copy CoopClient's exefs\\subsdk3 into the Luminescent Platinum exefs folder"}[coopMod])

	v.CoopAge = -1
	statusPath := filepath.Join(data, "sdcard", "config", "pokemon-coop-status.txt")
	if st, err := os.Stat(statusPath); err == nil {
		if b, err := os.ReadFile(statusPath); err == nil && len(b) < 8192 {
			v.CoopStatus = strings.TrimSpace(string(b))
			v.CoopAge = int(time.Since(st.ModTime()).Seconds())
		}
	}

	v.Ready = true
	for _, ch := range v.Checks {
		if !ch.OK && ch.ID != "mega" && ch.ID != "coop" { // optional for playing
			v.Ready = false
		}
	}
	return v
}

// writeGameCoopConfig puts the co-op settings on Ryujinx's virtual SD card
// (sdmc:/config/pokemon-coop.ini) for the in-game client to read.
func writeGameCoopConfig(dataDir string, c config.Client) (string, error) {
	dir := filepath.Join(dataDir, "sdcard", "config")
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return "", err
	}
	p := filepath.Join(dir, "pokemon-coop.ini")
	return p, config.SaveClient(p, config.Client{Address: c.Address, Room: c.Room, Name: c.Name, Password: c.Password})
}

// launchGame starts Ryujinx with the game. Ryujinx loads enabled mods itself.
func launchGame(c config.Client) error {
	var cmd *exec.Cmd
	if runtime.GOOS == "darwin" && strings.HasSuffix(c.Ryujinx, ".app") {
		cmd = exec.Command("open", "-a", c.Ryujinx, "--args", c.Game)
	} else {
		cmd = exec.Command(c.Ryujinx, c.Game)
		cmd.Dir = filepath.Dir(c.Ryujinx)
	}
	if err := cmd.Start(); err != nil {
		return fmt.Errorf("Couldn't start Ryujinx: %v", err)
	}
	go cmd.Wait()
	return nil
}
