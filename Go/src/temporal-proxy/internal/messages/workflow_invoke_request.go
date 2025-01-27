//-----------------------------------------------------------------------------
// FILE:		workflow_invoke_request.go
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
	"time"

	"temporal-proxy/internal"
)

type (

	// WorkflowInvokeRequest is WorkflowRequest of MessageType
	// WorkflowInvokeRequest.
	//
	// A WorkflowInvokeRequest contains a RequestId and a reference to a
	// WorkflowRequest struct in memory and ReplyType, which is
	// the corresponding MessageType for replying to this WorkflowRequest
	//
	// A WorkflowInvokeRequest will pass all of the given information
	// necessary to invoke a temporal workflow via the temporal client
	WorkflowInvokeRequest struct {
		*WorkflowRequest
	}
)

// NewWorkflowInvokeRequest is the default constructor for a WorkflowInvokeRequest
//
// returns *WorkflowInvokeRequest -> a reference to a newly initialized
// WorkflowInvokeRequest in memory
func NewWorkflowInvokeRequest() *WorkflowInvokeRequest {
	request := new(WorkflowInvokeRequest)
	request.WorkflowRequest = NewWorkflowRequest()
	request.SetType(internal.WorkflowInvokeRequest)
	request.SetReplyType(internal.WorkflowInvokeReply)

	return request
}

// GetName gets a WorkflowInvokeRequest's Name value
// from its properties map
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's Name
func (request *WorkflowInvokeRequest) GetName() *string {
	return request.GetStringProperty("Name")
}

// SetName sets a WorkflowInvokeRequest's Name value
// in its properties map
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *WorkflowInvokeRequest) SetName(value *string) {
	request.SetStringProperty("Name", value)
}

// GetArgs gets a WorkflowInvokeRequest's Args field
// from its properties map.  Args is a []byte holding the arguments
// for invoking a specific workflow
//
// returns []byte -> a []byte of representing workflow parameters
// or arguments for invoking
func (request *WorkflowInvokeRequest) GetArgs() []byte {
	return request.GetBytesProperty("Args")
}

// SetArgs sets an WorkflowInvokeRequest's Args field
// from its properties map.  Args is a []byte holding the arguments
// for invoking a specific workflow
//
// param value []byte -> []byte of representing workflow parameters
// or arguments for invoking
func (request *WorkflowInvokeRequest) SetArgs(value []byte) {
	request.SetBytesProperty("Args", value)
}

// GetWorkflowID gets a WorkflowInvokeRequest's WorkflowID value
// from its properties map. The original workflow ID.
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's WorkflowID
func (request *WorkflowInvokeRequest) GetWorkflowID() *string {
	return request.GetStringProperty("WorkflowId")
}

// SetWorkflowID sets an WorkflowInvokeRequest's WorkflowID value
// in its properties map. The original workflow ID.
//
// param value *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's WorkflowID
func (request *WorkflowInvokeRequest) SetWorkflowID(value *string) {
	request.SetStringProperty("WorkflowId", value)
}

// GetWorkflowType gets a WorkflowInvokeRequest's WorkflowType value
// from its properties map. The original workflow Type.
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's WorkflowType
func (request *WorkflowInvokeRequest) GetWorkflowType() *string {
	return request.GetStringProperty("WorkflowType")
}

// SetWorkflowType sets an WorkflowInvokeRequest's WorkflowType value
// in its properties map. The original workflow Type.
//
// param value *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's WorkflowType
func (request *WorkflowInvokeRequest) SetWorkflowType(value *string) {
	request.SetStringProperty("WorkflowType", value)
}

// GetRunID gets a WorkflowInvokeRequest's RunID value
// from its properties map. The workflow run ID.
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's RunID
func (request *WorkflowInvokeRequest) GetRunID() *string {
	return request.GetStringProperty("RunId")
}

// SetRunID sets a WorkflowInvokeRequest's RunID value
// in its properties map. The workflow run ID.
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *WorkflowInvokeRequest) SetRunID(value *string) {
	request.SetStringProperty("RunId", value)
}

// GetNamespace gets a WorkflowInvokeRequest's Namespace value
// from its properties map. The namespace where the workflow is executing.
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's Namespace
func (request *WorkflowInvokeRequest) GetNamespace() *string {
	return request.GetStringProperty("Namespace")
}

// SetNamespace sets a WorkflowInvokeRequest's Namespace value
// in its properties map. The namespace where the workflow is executing.
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *WorkflowInvokeRequest) SetNamespace(value *string) {
	request.SetStringProperty("Namespace", value)
}

// GetTaskQueue gets a WorkflowInvokeRequest's TaskQueue value
// from its properties map. The taskqueue where the workflow is executing.
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's TaskQueue
func (request *WorkflowInvokeRequest) GetTaskQueue() *string {
	return request.GetStringProperty("TaskQueue")
}

// SetTaskQueue sets a WorkflowInvokeRequest's TaskQueue value
// in its properties map. The taskqueue where the workflow is executing.
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *WorkflowInvokeRequest) SetTaskQueue(value *string) {
	request.SetStringProperty("TaskQueue", value)
}

// GetExecutionStartToCloseTimeout gets a WorkflowInvokeRequest's
// ExecutionStartToCloseTimeout property in its properties map.
// This is the The maximum duration the workflow is allowed to run.
//
// returns time.Duration -> the The maximum duration the workflow is allowed to run
func (request *WorkflowInvokeRequest) GetExecutionStartToCloseTimeout() time.Duration {
	return request.GetTimeSpanProperty("ExecutionStartToCloseTimeout")
}

// SetExecutionStartToCloseTimeout sets a WorkflowInvokeRequest's
// ExecutionStartToCloseTimeout property in its properties map.
// This is the The maximum duration the workflow is allowed to run.
//
// param value time.Duration -> the The maximum duration the workflow is allowed to run
func (request *WorkflowInvokeRequest) SetExecutionStartToCloseTimeout(value time.Duration) {
	request.SetTimeSpanProperty("ExecutionStartToCloseTimeout", value)
}

// GetReplayStatus gets the ReplayStatus from a WorkflowInvokeRequest's properties
// map. For workflow requests related to an executing workflow,
// this will indicate the current history replay state.
//
// returns internal.ReplayStatus -> the current history replay
// state of a workflow
func (request *WorkflowInvokeRequest) GetReplayStatus() internal.ReplayStatus {
	replayStatusPtr := request.GetStringProperty("ReplayStatus")
	if replayStatusPtr == nil {
		return internal.ReplayStatusUnspecified
	}
	replayStatus := internal.StringToReplayStatus(*replayStatusPtr)

	return replayStatus
}

// SetReplayStatus sets the ReplayStatus in a WorkflowInvokeRequest's properties
// map. For workflow requests related to an executing workflow,
// this will indicate the current history replay state.
//
// param value internal.ReplayStatus -> the current history replay
// state of a workflow
func (request *WorkflowInvokeRequest) SetReplayStatus(value internal.ReplayStatus) {
	status := value.String()
	request.SetStringProperty("ReplayStatus", &status)
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from WorkflowRequest.Clone()
func (request *WorkflowInvokeRequest) Clone() IProxyMessage {
	workflowInvokeRequest := NewWorkflowInvokeRequest()
	var messageClone IProxyMessage = workflowInvokeRequest
	request.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from WorkflowRequest.CopyTo()
func (request *WorkflowInvokeRequest) CopyTo(target IProxyMessage) {
	request.WorkflowRequest.CopyTo(target)
	if v, ok := target.(*WorkflowInvokeRequest); ok {
		v.SetName(request.GetName())
		v.SetArgs(request.GetArgs())
		v.SetNamespace(request.GetNamespace())
		v.SetWorkflowID(request.GetWorkflowID())
		v.SetWorkflowType(request.GetWorkflowType())
		v.SetRunID(request.GetRunID())
		v.SetTaskQueue(request.GetTaskQueue())
		v.SetExecutionStartToCloseTimeout(request.GetExecutionStartToCloseTimeout())
		v.SetReplayStatus(request.GetReplayStatus())
	}
}
