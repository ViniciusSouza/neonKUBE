package messages

import (
	"github.com/loopieio/cadence-proxy/internal/cadence/cadenceerrors"
	messagetypes "github.com/loopieio/cadence-proxy/internal/messages/types"
)

type (

	// StopWorkerReply is a ProxyReply of MessageType
	// StopWorkerReply It holds a reference to a
	// ProxyReply in memory
	StopWorkerReply struct {
		*ProxyReply
	}
)

// NewStopWorkerReply is the default constructor for
// a StopWorkerReply
//
// returns *StopWorkerReply -> pointer to a newly initialized
// StopWorkerReply in memory
func NewStopWorkerReply() *StopWorkerReply {
	reply := new(StopWorkerReply)
	reply.ProxyReply = NewProxyReply()
	reply.Type = messagetypes.StopWorkerReply

	return reply
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from ProxyMessage.Clone()
func (reply *StopWorkerReply) Clone() IProxyMessage {
	stopWorkerReply := NewStopWorkerReply()
	var messageClone IProxyMessage = stopWorkerReply
	reply.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from ProxyMessage.CopyTo()
func (reply *StopWorkerReply) CopyTo(target IProxyMessage) {
	reply.ProxyReply.CopyTo(target)
}

// SetProxyMessage inherits docs from ProxyMessage.SetProxyMessage()
func (reply *StopWorkerReply) SetProxyMessage(value *ProxyMessage) {
	reply.ProxyReply.SetProxyMessage(value)
}

// GetProxyMessage inherits docs from ProxyMessage.GetProxyMessage()
func (reply *StopWorkerReply) GetProxyMessage() *ProxyMessage {
	return reply.ProxyReply.GetProxyMessage()
}

// GetRequestID inherits docs from ProxyMessage.GetRequestID()
func (reply *StopWorkerReply) GetRequestID() int64 {
	return reply.ProxyReply.GetRequestID()
}

// SetRequestID inherits docs from ProxyMessage.SetRequestID()
func (reply *StopWorkerReply) SetRequestID(value int64) {
	reply.ProxyReply.SetRequestID(value)
}

// -------------------------------------------------------------------------
// IProxyReply interface methods for implementing the IProxyReply interface

// GetError inherits docs from ProxyReply.GetError()
func (reply *StopWorkerReply) GetError() *cadenceerrors.CadenceError {
	return reply.ProxyReply.GetError()
}

// SetError inherits docs from ProxyReply.SetError()
func (reply *StopWorkerReply) SetError(value *cadenceerrors.CadenceError) {
	reply.ProxyReply.SetError(value)
}