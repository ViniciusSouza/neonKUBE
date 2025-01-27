//-----------------------------------------------------------------------------
// FILE:		workflow_reply.go
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

package handlers

import (
	"errors"
	"os"
	"time"

	"go.temporal.io/sdk/workflow"
	"go.uber.org/zap"

	"temporal-proxy/internal"
	"temporal-proxy/internal/messages"
)

// -------------------------------------------------------------------------
// Workflow message types

func handleWorkflowInvokeReply(reply *messages.WorkflowInvokeReply, op *Operation) error {
	defer WorkflowContexts.Remove(op.GetContextID())

	requestID := reply.GetRequestID()
	contextID := op.GetContextID()
	clientID := reply.GetClientID()
	Logger.Debug("Settling Workflow",
		zap.Int64("ClientId", clientID),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", requestID),
		zap.Int("ProcessId", os.Getpid()))

	// WorkflowContext at the specified WorflowContextID
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		return internal.ErrEntityNotExist
	}

	workflowName := *wectx.GetWorkflowName()
	Logger.Debug("WorkflowInfo", zap.String("Workflow", workflowName))

	// check for ForceReplay
	if reply.GetForceReplay() {
		return op.SendChannel(nil, internal.NewTemporalError(errors.New("force-replay")))
	}

	// check for ContinueAsNew
	if reply.GetContinueAsNew() {
		continueContext := wectx.GetContext()
		if reply.GetContinueAsNewNamespace() != nil {
			continueContext = workflow.WithWorkflowNamespace(continueContext, *reply.GetContinueAsNewNamespace())
		}
		if reply.GetContinueAsNewTaskQueue() != nil {
			continueContext = workflow.WithTaskQueue(continueContext, *reply.GetContinueAsNewTaskQueue())
		}
		if reply.GetContinueAsNewExecutionStartToCloseTimeout() > 0 {
			continueContext = workflow.WithStartToCloseTimeout(continueContext, time.Duration(reply.GetContinueAsNewExecutionStartToCloseTimeout()))
		}
		if reply.GetContinueAsNewScheduleToCloseTimeout() > 0 {
			continueContext = workflow.WithScheduleToCloseTimeout(continueContext, time.Duration(reply.GetContinueAsNewScheduleToCloseTimeout()))
		}
		if reply.GetContinueAsNewScheduleToStartTimeout() > 0 {
			continueContext = workflow.WithScheduleToStartTimeout(continueContext, time.Duration(reply.GetContinueAsNewScheduleToStartTimeout()))
		}
		if reply.GetContinueAsNewStartToCloseTimeout() > 0 {
			continueContext = workflow.WithStartToCloseTimeout(continueContext, time.Duration(reply.GetContinueAsNewStartToCloseTimeout()))
		}
		continueAsNewWorkflow := workflowName
		if reply.GetContinueAsNewWorkflow() != nil {
			continueAsNewWorkflow = *reply.GetContinueAsNewWorkflow()
		}
		continueError := workflow.NewContinueAsNewError(continueContext, continueAsNewWorkflow, reply.GetContinueAsNewArgs())

		return op.SendChannel(continueError, nil)
	}

	// get result
	var result interface{} = reply.GetResult()

	// get error values
	err := reply.GetError()

	// canceled error case
	if internal.IsCancelledError(err) {
		result = workflow.ErrCanceled
		err = nil
	}

	// set the reply
	return op.SendChannel(result, err)
}

func handleWorkflowSignalInvokeReply(reply *messages.WorkflowSignalInvokeReply, op *Operation) error {
	requestID := reply.GetRequestID()
	contextID := op.GetContextID()
	Logger.Debug("Settling Signal",
		zap.Int64("ClientId", reply.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", requestID),
		zap.Int("ProcessId", os.Getpid()))

	// WorkflowContext at the specified WorflowContextID
	if wectx := WorkflowContexts.Get(contextID); wectx == nil {
		return internal.ErrEntityNotExist
	}

	// set the reply
	return op.SendChannel(true, reply.GetError())
}

func handleWorkflowQueryInvokeReply(reply *messages.WorkflowQueryInvokeReply, op *Operation) error {
	requestID := reply.GetRequestID()
	contextID := op.GetContextID()
	Logger.Debug("Settling Query",
		zap.Int64("ClientId", reply.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", requestID),
		zap.Int("ProcessId", os.Getpid()))

	// WorkflowContext at the specified WorflowContextID
	if wectx := WorkflowContexts.Get(contextID); wectx == nil {
		return internal.ErrEntityNotExist
	}

	// set the reply
	return op.SendChannel(reply.GetResult(), reply.GetError())
}

func handleWorkflowFutureReadyReply(reply *messages.WorkflowFutureReadyReply, op *Operation) error {
	requestID := reply.GetRequestID()
	contextID := op.GetContextID()
	Logger.Debug("Settling Future ACK",
		zap.Int64("ClientId", reply.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", requestID),
		zap.Int("ProcessId", os.Getpid()))

	// set the reply
	return op.SendChannel(true, nil)
}
