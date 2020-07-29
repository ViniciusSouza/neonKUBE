//-----------------------------------------------------------------------------
// FILE:		activity_get_result_reply.go
// CONTRIBUTOR: John C Burns
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

package messages

import (
	internal "temporal-proxy/internal"
	proxytemporal "temporal-proxy/internal/temporal"
	proxyerror "temporal-proxy/internal/temporal/error"
)

type (

	// ActivityGetResultReply is a ActivityReply of MessageType
	// ActivityGetResultReply.  It holds a reference to a ActivityReply in memory
	// and is the reply type to a ActivityExecuteRequest
	ActivityGetResultReply struct {
		*ActivityReply
	}
)

// NewActivityGetResultReply is the default constructor for
// a ActivityGetResultReply
//
// returns *ActivityGetResultReply -> a pointer to a newly initialized
// ActivityGetResultReply in memory
func NewActivityGetResultReply() *ActivityGetResultReply {
	reply := new(ActivityGetResultReply)
	reply.ActivityReply = NewActivityReply()
	reply.SetType(internal.ActivityGetResultReply)

	return reply
}

// GetResult gets the Activity execution result or nil
// from a ActivityGetResultReply's properties map.
//
// returns []byte -> the activity result encoded as bytes.
func (reply *ActivityGetResultReply) GetResult() []byte {
	return reply.GetBytesProperty("Result")
}

// SetResult sets the Activity execution result or nil
// in a ActivityGetResultReply's properties map.
//
// param value []byte -> the activity result encoded as bytes.
func (reply *ActivityGetResultReply) SetResult(value []byte) {
	reply.SetBytesProperty("Result", value)
}

// GetReplayStatus gets the ReplayStatus from a ActivityGetResultReply's properties
// map.
//
// returns proxytemporal.ReplayStatus -> the current history replay
// state of an activity
func (reply *ActivityGetResultReply) GetReplayStatus() proxytemporal.ReplayStatus {
	replayStatusPtr := reply.GetStringProperty("ReplayStatus")
	if replayStatusPtr == nil {
		return proxytemporal.ReplayStatusUnspecified
	}
	replayStatus := proxytemporal.StringToReplayStatus(*replayStatusPtr)

	return replayStatus
}

// SetReplayStatus sets the ReplayStatus in a WorkflowInvokeRequest's properties
// map.
//
// param value proxytemporal.ReplayStatus -> the current history replay
// state of an activity
func (reply *ActivityGetResultReply) SetReplayStatus(value proxytemporal.ReplayStatus) {
	status := value.String()
	reply.SetStringProperty("ReplayStatus", &status)
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Build inherits docs from ActivityReply.Build()
func (reply *ActivityGetResultReply) Build(e *proxyerror.TemporalError, result ...interface{}) {
	reply.ActivityReply.Build(e)
	if len(result) > 0 {
		if v, ok := result[0].([]interface{}); ok {
			if _v, _ok := v[0].([]byte); _ok {
				reply.SetResult(_v)
			}
			if _v, _ok := v[1].(proxytemporal.ReplayStatus); _ok {
				reply.SetReplayStatus(_v)
			}
		}
	}
}

// Clone inherits docs from ProxyMessage.Clone()
func (reply *ActivityGetResultReply) Clone() IProxyMessage {
	activityGetResultReply := NewActivityGetResultReply()
	var messageClone IProxyMessage = activityGetResultReply
	reply.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from ProxyMessage.CopyTo()
func (reply *ActivityGetResultReply) CopyTo(target IProxyMessage) {
	reply.ActivityReply.CopyTo(target)
	if v, ok := target.(*ActivityGetResultReply); ok {
		v.SetResult(reply.GetResult())
		v.SetReplayStatus(reply.GetReplayStatus())
	}
}