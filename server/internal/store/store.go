// Package store persists server-wide game state (legendary owners, badges)
// to a small JSON file so it survives restarts.
package store

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"time"
)

// Legend is a caught legendary and who owns it.
type Legend struct {
	OwnerKey  string    `json:"owner_key"`
	OwnerName string    `json:"owner_name"`
	Since     time.Time `json:"since"`
}

// Achievement is a beaten gym / Elite Four member / Champion.
type Achievement struct {
	Kind   uint8  `json:"kind"`
	Target uint16 `json:"target"`
}

type data struct {
	Version  int                          `json:"version"`
	Legends  map[string]map[uint16]Legend `json:"legends"` // room code -> species -> owner
	Progress map[string][]Achievement     `json:"progress"`
}

// Store is not safe for concurrent use; the server hub owns it.
type Store struct {
	path string // empty = memory only
	d    data
}

// Open loads path, or starts empty if it doesn't exist. path "" keeps state in memory.
func Open(path string) (*Store, error) {
	s := &Store{path: path, d: data{Version: 1, Legends: map[string]map[uint16]Legend{}, Progress: map[string][]Achievement{}}}
	if path == "" {
		return s, nil
	}
	b, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return s, nil
	}
	if err != nil {
		return nil, err
	}
	if err := json.Unmarshal(b, &s.d); err != nil {
		return nil, err
	}
	if s.d.Legends == nil {
		s.d.Legends = map[string]map[uint16]Legend{}
	}
	if s.d.Progress == nil {
		s.d.Progress = map[string][]Achievement{}
	}
	return s, nil
}

// save writes atomically (temp file + rename) so a crash never leaves a half-written file.
func (s *Store) save() error {
	if s.path == "" {
		return nil
	}
	b, err := json.MarshalIndent(s.d, "", "  ")
	if err != nil {
		return err
	}
	tmp, err := os.CreateTemp(filepath.Dir(s.path), ".coop-data-*.tmp")
	if err != nil {
		return err
	}
	if _, err := tmp.Write(b); err != nil {
		tmp.Close()
		os.Remove(tmp.Name())
		return err
	}
	if err := tmp.Sync(); err != nil {
		tmp.Close()
		os.Remove(tmp.Name())
		return err
	}
	tmp.Close()
	return os.Rename(tmp.Name(), s.path)
}

// Legendaries are tracked per room code: each group has its own one-of-each.

func (s *Store) Legend(room string, species uint16) (Legend, bool) {
	l, ok := s.d.Legends[room][species]
	return l, ok
}

func (s *Store) Legends(room string) map[uint16]Legend { return s.d.Legends[room] }

func (s *Store) SetLegend(room string, species uint16, l Legend) error {
	if s.d.Legends[room] == nil {
		s.d.Legends[room] = map[uint16]Legend{}
	}
	s.d.Legends[room][species] = l
	return s.save()
}

func (s *Store) DeleteLegend(room string, species uint16) error {
	delete(s.d.Legends[room], species)
	if len(s.d.Legends[room]) == 0 {
		delete(s.d.Legends, room)
	}
	return s.save()
}

func (s *Store) Progress(key string) []Achievement { return s.d.Progress[key] }

func (s *Store) HasAchievement(key string, a Achievement) bool {
	for _, x := range s.d.Progress[key] {
		if x == a {
			return true
		}
	}
	return false
}

// AddAchievement records a win; returns false if it was already recorded.
func (s *Store) AddAchievement(key string, a Achievement) (bool, error) {
	for _, x := range s.d.Progress[key] {
		if x == a {
			return false, nil
		}
	}
	s.d.Progress[key] = append(s.d.Progress[key], a)
	return true, s.save()
}
