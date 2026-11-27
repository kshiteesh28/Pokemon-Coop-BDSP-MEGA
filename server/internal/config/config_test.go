package config

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestParse(t *testing.T) {
	in := "\uFEFF# comment\r\n[client]\r\naddress = 100.1.2.3:4100\r\nROOM=aaroh\r\n; other comment\r\nname = \"Aaroh M\"\r\npassword =\r\n"
	m, err := Parse(strings.NewReader(in))
	if err != nil {
		t.Fatal(err)
	}
	want := map[string]string{"address": "100.1.2.3:4100", "room": "aaroh", "name": "Aaroh M", "password": ""}
	for k, v := range want {
		if m[k] != v {
			t.Errorf("%s = %q, want %q", k, m[k], v)
		}
	}
}

func TestParseError(t *testing.T) {
	if _, err := Parse(strings.NewReader("address 1.2.3.4")); err == nil {
		t.Fatal("expected error for line without =")
	}
}

func TestLoadClient(t *testing.T) {
	dir := t.TempDir()
	if _, ok, err := LoadClient(filepath.Join(dir, "missing.ini")); ok || err != nil {
		t.Fatalf("missing file: ok=%v err=%v", ok, err)
	}
	p := filepath.Join(dir, "coop.ini")
	os.WriteFile(p, []byte("address=1.2.3.4:4100\nroom=X\nname=N\npassword=pw\n"), 0o644)
	c, ok, err := LoadClient(p)
	if !ok || err != nil || c != (Client{"1.2.3.4:4100", "X", "N", "pw"}) {
		t.Fatalf("%+v %v %v", c, ok, err)
	}
}
