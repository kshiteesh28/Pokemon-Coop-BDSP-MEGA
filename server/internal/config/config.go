// Package config reads coop.ini, the one settings file shared by the test
// client and (later) the in-game co-op client.
//
//	# coop.ini
//	address  = 100.67.109.85:4100
//	room     = AAROH
//	name     = Aaroh
//	password =
//
// Blank lines, [section] headers and lines starting with # or ; are ignored.
// Keys are case-insensitive; values are trimmed and may be wrapped in quotes.
package config

import (
	"bufio"
	"fmt"
	"io"
	"os"
	"strings"
)

type Client struct {
	Address  string
	Room     string
	Name     string
	Password string
	Ryujinx  string // path to Ryujinx.exe / Ryujinx.app (used by the app's Play button)
	Game     string // path to the Brilliant Diamond game file (.nsp/.xci)
}

// Parse reads key = value lines.
func Parse(r io.Reader) (map[string]string, error) {
	out := map[string]string{}
	sc := bufio.NewScanner(r)
	line := 0
	for sc.Scan() {
		line++
		s := strings.TrimSpace(sc.Text())
		if line == 1 {
			s = strings.TrimPrefix(s, "\uFEFF") // Notepad's UTF-8 BOM
		}
		if s == "" || s[0] == '#' || s[0] == ';' || s[0] == '[' {
			continue
		}
		k, v, ok := strings.Cut(s, "=")
		if !ok {
			return nil, fmt.Errorf("line %d: expected key = value, got %q", line, s)
		}
		v = strings.TrimSpace(v)
		if len(v) >= 2 && (v[0] == '"' && v[len(v)-1] == '"' || v[0] == '\'' && v[len(v)-1] == '\'') {
			v = v[1 : len(v)-1]
		}
		out[strings.ToLower(strings.TrimSpace(k))] = v
	}
	return out, sc.Err()
}

// LoadClient reads path. A missing file returns (zero Client, false, nil).
func LoadClient(path string) (Client, bool, error) {
	f, err := os.Open(path)
	if os.IsNotExist(err) {
		return Client{}, false, nil
	}
	if err != nil {
		return Client{}, false, err
	}
	defer f.Close()
	m, err := Parse(f)
	if err != nil {
		return Client{}, false, fmt.Errorf("%s: %w", path, err)
	}
	return Client{Address: m["address"], Room: m["room"], Name: m["name"], Password: m["password"], Ryujinx: m["ryujinx"], Game: m["game"]}, true, nil
}

// SaveClient writes c to path in the standard coop.ini layout (Windows line
// endings so Notepad shows it nicely; the parser accepts either).
func SaveClient(path string, c Client) error {
	text := "# Pokemon co-op settings (used by the launcher, join scripts and the game)\r\n" +
		"address  = " + c.Address + "\r\n" +
		"room     = " + c.Room + "\r\n" +
		"name     = " + c.Name + "\r\n" +
		"password = " + c.Password + "\r\n"
	if c.Ryujinx != "" || c.Game != "" {
		text += "\r\n# used by the app's Play button\r\n" +
			"ryujinx  = " + c.Ryujinx + "\r\n" +
			"game     = " + c.Game + "\r\n"
	}
	return os.WriteFile(path, []byte(text), 0o600)
}
