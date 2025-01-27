//-----------------------------------------------------------------------------
// FILE:		activity_execute_request.go
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
	"go.temporal.io/sdk/workflow"

	internal "temporal-proxy/internal"
)

type (

	// ActivityExecuteRequest is an ActivityRequest of MessageType
	// ActivityExecuteRequest.
	//
	// A ActivityExecuteRequest contains a reference to a
	// ActivityRequest struct in memory and ReplyType, which is
	// the corresponding MessageType for replying to this ActivityRequest
	//
	// Starts a workflow activity.
	ActivityExecuteRequest struct {
		*ActivityRequest
	}
)

// NewActivityExecuteRequest is the default constructor for a ActivityExecuteRequest
//
// returns *ActivityExecuteRequest -> a pointer to a newly initialized ActivityExecuteRequest
// in memory
func NewActivityExecuteRequest() *ActivityExecuteRequest {
	request := new(ActivityExecuteRequest)
	request.ActivityRequest = NewActivityRequest()
	request.SetType(internal.ActivityExecuteRequest)
	request.SetReplyType(internal.ActivityExecuteReply)

	return request
}

// GetActivity gets a ActivityExecuteRequest's Activity field
// from its properties map.  Specifies the activity to
// be executed.
//
// returns *string -> *string representing the activity of the
// activity to be executed
func (request *ActivityExecuteRequest) GetActivity() *string {
	return request.GetStringProperty("Activity")
}

// SetActivity sets an ActivityExecuteRequest's Activity field
// from its properties map.  Specifies the activity to
// be executed.
//
// param value *string -> *string representing the activity of the
// activity to be executed
func (request *ActivityExecuteRequest) SetActivity(value *string) {
	request.SetStringProperty("Activity", value)
}

// GetArgs gets a ActivityExecuteRequest's Args field
// from its properties map.  Args is a []byte that hold the arguments
// for executing a specific workflow activity
//
// returns []byte -> []byte representing workflow activity parameters or arguments
// for executing
func (request *ActivityExecuteRequest) GetArgs() []byte {
	return request.GetBytesProperty("Args")
}

// SetArgs sets an ActivityExecuteRequest's Args field
// from its properties map.  Args is a []byte that hold the arguments
// for executing a specific workflow activity
//
// param value []byte -> []byte representing workflow activity parameters or arguments
// for executing
func (request *ActivityExecuteRequest) SetArgs(value []byte) {
	request.SetBytesProperty("Args", value)
}

// GetOptions gets a ActivityExecutionRequest's start options
// used to execute a temporal workflow activity via the temporal workflow client
//
// returns client.StartActivityOptions -> a temporal client struct that contains the
// options for executing a workflow activity
func (request *ActivityExecuteRequest) GetOptions() *workflow.ActivityOptions {
	opts := new(workflow.ActivityOptions)
	err := request.GetJSONProperty("Options", opts)
	if err != nil {
		return nil
	}

	return opts
}

// SetOptions sets a ActivityExecutionRequest's start options
// used to execute a temporal workflow activity via the temporal workflow client
//
// param value client.StartActivityOptions -> a temporal client struct that contains the
// options for executing a workflow activity to be set in the ActivityExecutionRequest's
// properties map
func (request *ActivityExecuteRequest) SetOptions(value *workflow.ActivityOptions) {
	request.SetJSONProperty("Options", value)
}

// GetNamespace gets a ActivityExecuteRequest's Namespace value
// from its properties map
//
// returns *string -> pointer to a string in memory holding the value
// of a ActivityExecuteRequest's Namespace
func (request *ActivityExecuteRequest) GetNamespace() *string {
	return request.GetStringProperty("Namespace")
}

// SetNamespace sets a ActivityExecuteRequest's Namespace value
// in its properties map.
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *ActivityExecuteRequest) SetNamespace(value *string) {
	request.SetStringProperty("Namespace", value)
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from ActivityRequest.Clone()
func (request *ActivityExecuteRequest) Clone() IProxyMessage {
	activityExecuteRequest := NewActivityExecuteRequest()
	var messageClone IProxyMessage = activityExecuteRequest
	request.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from ActivityRequest.CopyTo()
func (request *ActivityExecuteRequest) CopyTo(target IProxyMessage) {
	request.ActivityRequest.CopyTo(target)
	if v, ok := target.(*ActivityExecuteRequest); ok {
		v.SetArgs(request.GetArgs())
		v.SetOptions(request.GetOptions())
		v.SetActivity(request.GetActivity())
		v.SetNamespace(request.GetNamespace())
	}
}
