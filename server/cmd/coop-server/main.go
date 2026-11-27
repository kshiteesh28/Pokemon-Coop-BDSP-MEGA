// coop-server is the N-player shared-world relay for Pokemon-Coop-BDSP-MEGA.
//
//	coop-server -listen :4100 -max-players 4 -password secret
package main

import (
	"context"
	"flag"
	"log"
	"net"
	"os"
	"os/signal"
	"strconv"
	"strings"
	"syscall"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/internal/coop"
)

func main() {
	listen := flag.String("listen", envOr("COOP_LISTEN", ":4100"), "TCP address to listen on")
	maxPlayers := flag.Int("max-players", 4, "max players per room")
	maxRooms := flag.Int("max-rooms", 16, "max simultaneous rooms")
	maxConns := flag.Int("max-conns", 64, "max open TCP connections")
	tick := flag.Int("tick", 20, "snapshot rate in Hz")
	timeout := flag.Duration("timeout", 5*time.Second, "disconnect clients silent for this long")
	verbose := flag.Bool("v", false, "verbose logging")
	dataPath := flag.String("data", envOr("COOP_DATA", "coop-data.json"), "file that stores legendary owners and badges")
	legendList := flag.String("legendaries", "", "comma-separated National Dex numbers to track (default: built-in BDSP list)")
	flag.Parse()

	password := os.Getenv("COOP_PASSWORD") // env, not a flag, so it doesn't show in `ps`

	ln, err := net.Listen("tcp", *listen)
	if err != nil {
		log.Fatalf("listen %s: %v", *listen, err)
	}
	log.Printf("coop-server listening on %s (max %d players/room, %d Hz, password %s)",
		ln.Addr(), *maxPlayers, *tick, map[bool]string{true: "on", false: "off"}[password != ""])

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	var legends []uint16
	if *legendList != "" {
		for _, f := range strings.Split(*legendList, ",") {
			n, err := strconv.ParseUint(strings.TrimSpace(f), 10, 16)
			if err != nil {
				log.Fatalf("bad -legendaries entry %q", f)
			}
			legends = append(legends, uint16(n))
		}
	}
	srv, err := coop.New(coop.Config{
		MaxPlayersPerRoom: *maxPlayers, MaxRooms: *maxRooms, MaxConns: *maxConns,
		Password: password, TickHz: *tick, Timeout: *timeout, Verbose: *verbose,
		DataPath: *dataPath, Legendaries: legends,
	})
	if err != nil {
		log.Fatalf("loading %s: %v", *dataPath, err)
	}
	log.Printf("saving legendary owners and badges to %s", *dataPath)
	if err := srv.Serve(ctx, ln); err != nil {
		log.Fatal(err)
	}
	log.Print("shut down")
}

func envOr(k, def string) string {
	if v := os.Getenv(k); v != "" {
		return v
	}
	return def
}
