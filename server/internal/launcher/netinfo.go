package launcher

import (
	"net"
	"sort"
	"strings"
)

// Addr is an address friends can use to reach this machine.
type Addr struct {
	Kind  string `json:"kind"` // "tailscale" or "lan"
	IP    string `json:"ip"`
	Iface string `json:"iface"`
}

var cgnat = mustCIDR("100.64.0.0/10") // Tailscale hands out addresses from this range

func mustCIDR(s string) *net.IPNet {
	_, n, err := net.ParseCIDR(s)
	if err != nil {
		panic(err)
	}
	return n
}

// DetectAddrs lists this machine's Tailscale and private LAN IPv4 addresses,
// Tailscale first. Virtual adapters (VirtualBox, Hyper-V, WSL, Docker) are skipped.
func DetectAddrs() []Addr {
	ifaces, err := net.Interfaces()
	if err != nil {
		return nil
	}
	var out []Addr
	for _, ifc := range ifaces {
		if ifc.Flags&net.FlagUp == 0 || ifc.Flags&net.FlagLoopback != 0 {
			continue
		}
		name := strings.ToLower(ifc.Name)
		addrs, _ := ifc.Addrs()
		for _, a := range addrs {
			ipn, ok := a.(*net.IPNet)
			if !ok {
				continue
			}
			ip := ipn.IP.To4()
			if ip == nil {
				continue
			}
			switch {
			case cgnat.Contains(ip):
				out = append(out, Addr{Kind: "tailscale", IP: ip.String(), Iface: ifc.Name})
			case ip.IsPrivate() && !isVirtual(name):
				out = append(out, Addr{Kind: "lan", IP: ip.String(), Iface: ifc.Name})
			}
		}
	}
	sort.SliceStable(out, func(i, j int) bool { return out[i].Kind == "tailscale" && out[j].Kind != "tailscale" })
	return out
}

func isVirtual(name string) bool {
	for _, v := range []string{"virtualbox", "vmware", "vethernet", "hyper-v", "wsl", "docker", "vbox", "veth", "br-", "utun"} {
		if strings.Contains(name, v) {
			return true
		}
	}
	return false
}
