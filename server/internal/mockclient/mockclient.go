// Package mockclient is a minimal protocol client used by tests and coop-sim.
// It stands in for the Ryujinx/subsdk client while that side is being built.
package mockclient

import (
	"net"
	"sync"
	"time"

	"github.com/kshiteesh28/Pokemon-Coop-BDSP-MEGA/server/protocol"
)

type Packet struct {
	Type    protocol.MsgType
	Payload []byte
}

type Client struct {
	Conn    net.Conn
	Packets chan Packet // every packet received, in order
	mu      sync.Mutex
	seq     uint16
}

func Dial(addr string) (*Client, error) {
	conn, err := net.DialTimeout("tcp", addr, 5*time.Second)
	if err != nil {
		return nil, err
	}
	if tc, ok := conn.(*net.TCPConn); ok {
		tc.SetNoDelay(true)
	}
	c := &Client{Conn: conn, Packets: make(chan Packet, 4096)}
	go func() {
		buf := make([]byte, protocol.MaxPayload)
		defer close(c.Packets)
		for {
			h, p, err := protocol.ReadPacket(conn, buf)
			if err != nil {
				return
			}
			select {
			case c.Packets <- Packet{h.Type, append([]byte(nil), p...)}:
			default: // test consumer too slow; drop
			}
		}
	}()
	return c, nil
}

func (c *Client) Send(t protocol.MsgType, payload []byte) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.seq++
	c.Conn.SetWriteDeadline(time.Now().Add(5 * time.Second))
	_, err := c.Conn.Write(protocol.Encode(t, c.seq, payload))
	return err
}

func (c *Client) Close() error { return c.Conn.Close() }

// Hello sends HELLO_REQ and waits for HELLO_ACK.
func (c *Client) Hello(req protocol.HelloReq, timeout time.Duration) (protocol.HelloAck, error) {
	var ack protocol.HelloAck
	if err := c.Send(protocol.MsgHelloReq, req.Marshal()); err != nil {
		return ack, err
	}
	p, err := c.WaitFor(protocol.MsgHelloAck, timeout)
	if err != nil {
		return ack, err
	}
	return ack, ack.Unmarshal(p)
}

type timeoutErr struct{ t protocol.MsgType }

func (e timeoutErr) Error() string { return "timed out waiting for " + e.t.String() }

type closedErr struct{}

func (closedErr) Error() string { return "connection closed" }

// WaitFor discards packets until one of type t arrives.
func (c *Client) WaitFor(t protocol.MsgType, timeout time.Duration) ([]byte, error) {
	deadline := time.After(timeout)
	for {
		select {
		case p, ok := <-c.Packets:
			if !ok {
				return nil, closedErr{}
			}
			if p.Type == t {
				return p.Payload, nil
			}
		case <-deadline:
			return nil, timeoutErr{t}
		}
	}
}

// WaitClosed returns true if the server closes the connection within timeout.
func (c *Client) WaitClosed(timeout time.Duration) bool {
	deadline := time.After(timeout)
	for {
		select {
		case _, ok := <-c.Packets:
			if !ok {
				return true
			}
		case <-deadline:
			return false
		}
	}
}
