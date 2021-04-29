// -----------------------------------------------------------------------------
// FILE:		workflow_request.go
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
	"bytes"
	"context"
	"fmt"
	"os"
	"reflect"
	"time"

	"go.temporal.io/sdk/client"
	"go.temporal.io/sdk/converter"
	"go.temporal.io/sdk/worker"
	"go.temporal.io/sdk/workflow"
	"go.uber.org/zap"

	"temporal-proxy/internal"
	"temporal-proxy/internal/messages"
	proxyworkflow "temporal-proxy/internal/temporal/workflow"
)

// ----------------------------------------------------------------------
// IProxyRequest workflow message type handler methods

func handleWorkflowRegisterRequest(requestCtx context.Context, request *messages.WorkflowRegisterRequest) messages.IProxyReply {
	workflowName := *request.GetName()
	clientID := request.GetClientID()
	workerID := request.GetWorkerID()
	Logger.Debug("WorkflowRegisterRequest Received",
		zap.String("Workflow", workflowName),
		zap.Int64("WorkerId", workerID),
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowRegisterReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create workflow function
	workflowFunc := func(ctx workflow.Context, input []byte) ([]byte, error) {
		contextID := proxyworkflow.NextContextID()
		requestID := NextRequestID()
		Logger.Debug("Executing Workflow",
			zap.String("Workflow", workflowName),
			zap.Int64("ClientId", clientID),
			zap.Int64("WorkerId", workerID),
			zap.Int64("ContextId", contextID),
			zap.Int64("RequestId", requestID),
			zap.Int("ProcessId", os.Getpid()))

		// set the WorkflowContext in WorkflowContexts
		wectx := proxyworkflow.NewWorkflowContext(ctx)
		wectx.SetWorkflowName(&workflowName)
		contextID = WorkflowContexts.Add(contextID, wectx)

		// Send a WorkflowInvokeRequest to the Neon.Temporal Lib
		// temporal-client
		invokeRequest := messages.NewWorkflowInvokeRequest()
		invokeRequest.SetRequestID(requestID)
		invokeRequest.SetContextID(contextID)
		invokeRequest.SetArgs(input)
		invokeRequest.SetClientID(clientID)
		invokeRequest.SetWorkerID(workerID)

		// get the WorkflowInfo (Namespace, WorkflowID, RunID, WorkflowType,
		// TaskQueue, ExecutionStartToCloseTimeout)
		// from the context
		workflowInfo := workflow.GetInfo(ctx)
		invokeRequest.SetNamespace(&workflowInfo.Namespace)
		invokeRequest.SetWorkflowID(&workflowInfo.WorkflowExecution.ID)
		invokeRequest.SetRunID(&workflowInfo.WorkflowExecution.RunID)
		invokeRequest.SetWorkflowType(&workflowInfo.WorkflowType.Name)
		invokeRequest.SetTaskQueue(&workflowInfo.TaskQueueName)
		invokeRequest.SetExecutionStartToCloseTimeout(time.Duration(int64(workflowInfo.WorkflowExecutionTimeout) * int64(time.Second)))

		// set ReplayStatus
		setReplayStatus(ctx, invokeRequest)

		// create the Operation for this request and add it to the operations map
		op := NewOperation(requestID, invokeRequest)
		op.SetChannel(make(chan interface{}))
		op.SetContextID(contextID)
		Operations.Add(requestID, op)

		// send invokeRequest
		go sendMessage(invokeRequest)

		Logger.Debug("WorkflowInvokeRequest sent",
			zap.String("Workflow", workflowName),
			zap.Int64("ClientId", clientID),
			zap.Int64("WorkerId", workerID),
			zap.Int64("ContextId", contextID),
			zap.Int64("RequestId", requestID),
			zap.Int("ProcessId", os.Getpid()))

		// block and get result
		result := <-op.GetChannel()
		switch s := result.(type) {
		case error:
			if isForceReplayErr(s) {
				panic("force-replay")
			}

			Logger.Error("Workflow Failed With Error",
				zap.String("Workflow", workflowName),
				zap.Int64("ClientId", clientID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("ContextId", contextID),
				zap.Int64("RequestId", requestID),
				zap.Error(s),
				zap.Int("ProcessId", os.Getpid()))

			return nil, s

		case []byte:
			Logger.Info("Workflow Completed Successfully",
				zap.String("Workflow", workflowName),
				zap.Int64("ClientId", clientID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("ContextId", contextID),
				zap.Int64("RequestId", requestID),
				zap.ByteString("Result", s),
				zap.Int("ProcessId", os.Getpid()))

			return s, nil

		default:
			Logger.Error("Unexpected result type",
				zap.String("Workflow", workflowName),
				zap.Int64("ClientId", clientID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("ContextId", contextID),
				zap.Int64("RequestId", requestID),
				zap.Any("Result", s),
				zap.Int("ProcessId", os.Getpid()))

			return nil, fmt.Errorf("unexpected result type %v.  result must be an error or []byte", reflect.TypeOf(s))
		}
	}

	clientHelper.WorkflowRegister(workerID, workflowFunc, workflowName)
	Logger.Debug("workflow successfully registered", zap.String("WorkflowName", workflowName))
	reply.Build(nil)

	return reply
}

func handleWorkflowExecuteRequest(requestCtx context.Context, request *messages.WorkflowExecuteRequest) messages.IProxyReply {
	workflowName := *request.GetWorkflow()
	namespace := *request.GetNamespace()
	clientID := request.GetClientID()
	Logger.Debug("WorkflowExecuteRequest Received",
		zap.String("WorkflowName", workflowName),
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.String("Namespace", namespace),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowExecuteReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// check for options
	var opts client.StartWorkflowOptions
	if v := request.GetOptions(); v != nil {
		opts = *v
	}

	// signalwithstart the specified workflow
	workflowRun, err := clientHelper.ExecuteWorkflow(
		ctx,
		namespace,
		opts,
		workflowName,
		request.GetArgs())

	if err != nil {
		reply.Build(err)
		return reply
	}

	workflowExecution := workflow.Execution{
		ID:    workflowRun.GetID(),
		RunID: workflowRun.GetRunID(),
	}

	reply.Build(nil, &workflowExecution)

	return reply
}

func handleWorkflowCancelRequest(requestCtx context.Context, request *messages.WorkflowCancelRequest) messages.IProxyReply {
	workflowID := *request.GetWorkflowID()
	runID := *request.GetRunID()
	clientID := request.GetClientID()
	Logger.Debug("WorkflowCancelRequest Received",
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.String("WorkflowId", workflowID),
		zap.String("RunId", runID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowCancelReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context to cancel the workflow
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// cancel the specified workflow
	err := clientHelper.CancelWorkflow(
		ctx,
		workflowID,
		runID,
		*request.GetNamespace())

	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil)

	return reply
}

func handleWorkflowTerminateRequest(requestCtx context.Context, request *messages.WorkflowTerminateRequest) messages.IProxyReply {
	workflowID := *request.GetWorkflowID()
	runID := *request.GetRunID()
	clientID := request.GetClientID()
	Logger.Debug("WorkflowTerminateRequest Received",
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.String("WorkflowId", workflowID),
		zap.String("RunId", runID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowTerminateReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context to terminate the workflow
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// terminate the specified workflow
	err := clientHelper.TerminateWorkflow(
		ctx,
		*request.GetWorkflowID(),
		*request.GetRunID(),
		*request.GetNamespace(),
		*request.GetReason(),
		request.GetDetails())

	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil)

	return reply
}

func handleWorkflowSignalWithStartRequest(requestCtx context.Context, request *messages.WorkflowSignalWithStartRequest) messages.IProxyReply {
	workflow := *request.GetWorkflow()
	workflowID := *request.GetWorkflowID()
	clientID := request.GetClientID()
	Logger.Debug("WorkflowSignalWithStartRequest Received",
		zap.String("Workflow", workflow),
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.String("WorkflowId", workflowID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowSignalWithStartReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// signalwithstart the specified workflow
	execution, err := clientHelper.SignalWithStartWorkflow(
		ctx,
		workflowID,
		*request.GetNamespace(),
		*request.GetSignalName(),
		request.GetSignalArgs(),
		*request.GetOptions(),
		workflow,
		request.GetWorkflowArgs())

	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil, execution)

	return reply
}

func handleWorkflowSetCacheSizeRequest(requestCtx context.Context, request *messages.WorkflowSetCacheSizeRequest) messages.IProxyReply {
	Logger.Debug("WorkflowSetCacheSizeRequest Received",
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowSetCacheSizeReply
	reply := messages.CreateReplyMessage(request)

	// set the sticky workflow cache size
	worker.SetStickyWorkflowCacheSize(request.GetSize())
	reply.Build(nil)

	return reply
}

func handleWorkflowMutableRequest(requestCtx context.Context, request *messages.WorkflowMutableRequest) messages.IProxyReply {
	Logger.Debug("WorkflowMutableRequest Received",
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowMutableReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(request.GetContextID())
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set ReplayStatus
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)

	// f function for workflow.MutableSideEffect
	mutableFunc := func(ctx workflow.Context) interface{} {
		return request.GetResult()
	}

	// the equals function for workflow.MutableSideEffect
	equals := func(a, b interface{}) bool {
		if v, ok := a.(*internal.TemporalError); ok {
			if _v, _ok := b.(*internal.TemporalError); _ok {
				if v.GetType() == _v.GetType() &&
					v.Error() == _v.Error() {
					return true
				}
				return false
			}
			return false
		}

		if v, ok := a.([]byte); ok {
			if _v, _ok := b.([]byte); _ok {
				return bytes.Equal(v, _v)
			}
			return false
		}
		return false
	}

	// MutableSideEffect/SideEffect calls
	var value converter.EncodedValue
	if mutableID := request.GetMutableID(); mutableID != nil {
		value = workflow.MutableSideEffect(
			ctx,
			*mutableID,
			mutableFunc,
			equals)
	} else {
		value = workflow.SideEffect(ctx, mutableFunc)
	}

	// extract the result
	var result []byte
	err := value.Get(&result)

	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil, result)

	return reply
}

func handleWorkflowDescribeExecutionRequest(requestCtx context.Context, request *messages.WorkflowDescribeExecutionRequest) messages.IProxyReply {
	workflowID := *request.GetWorkflowID()
	runID := *request.GetRunID()
	clientID := request.GetClientID()
	Logger.Debug("WorkflowDescribeExecutionRequest Received",
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.String("WorkflowId", workflowID),
		zap.String("RunId", runID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowDescribeExecutionReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// DescribeWorkflow call to temporal client
	dwer, err := clientHelper.DescribeWorkflowExecution(
		ctx,
		workflowID,
		runID,
		*request.GetNamespace())

	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil, dwer)

	return reply
}

func handleWorkflowGetResultRequest(requestCtx context.Context, request *messages.WorkflowGetResultRequest) messages.IProxyReply {
	clientID := request.GetClientID()
	Logger.Debug("WorkflowGetResultRequest Received",
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowGetResultReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// call GetWorkflow
	workflowRun, err := clientHelper.GetWorkflow(
		ctx,
		*request.GetWorkflowID(),
		*request.GetRunID(),
		*request.GetNamespace())

	if err != nil {
		reply.Build(err)
		return reply
	}

	// get the result of WorkflowRun
	var result []byte
	err = workflowRun.Get(requestCtx, &result)
	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil, result)

	return reply
}

func handleWorkflowSignalSubscribeRequest(requestCtx context.Context, request *messages.WorkflowSignalSubscribeRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	clientID := request.GetClientID()
	workerID := request.GetWorkerID()
	signalName := *request.GetSignalName()
	Logger.Debug("WorkflowSignalSubscribeRequest Received",
		zap.String("SignalName", signalName),
		zap.Int64("ClientId", clientID),
		zap.Int64("WorkerId", workerID),
		zap.Int64("ContextId", request.GetContextID()),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowSignalSubscribeReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	ctx := wectx.GetContext()

	// create selector for receiving signals
	var signalArgs []byte
	selector := workflow.NewSelector(ctx)
	selector = selector.AddReceive(workflow.GetSignalChannel(ctx, signalName), func(channel workflow.ReceiveChannel, more bool) {
		channel.Receive(ctx, &signalArgs)
		Logger.Debug("SignalReceived",
			zap.String("Siganl", signalName),
			zap.Int64("ClientId", clientID),
			zap.ByteString("args", signalArgs))

		// create the WorkflowSignalInvokeRequest
		requestID := NextRequestID()
		invokeRequest := messages.NewWorkflowSignalInvokeRequest()
		invokeRequest.SetRequestID(requestID)
		invokeRequest.SetContextID(contextID)
		invokeRequest.SetSignalArgs(signalArgs)
		invokeRequest.SetSignalName(&signalName)
		invokeRequest.SetClientID(clientID)
		invokeRequest.SetWorkerID(workerID)

		// set ReplayStatus
		setReplayStatus(ctx, invokeRequest)

		// create the Operation for this request and add it to the operations map
		op := NewOperation(requestID, invokeRequest)
		op.SetChannel(make(chan interface{}))
		op.SetContextID(contextID)
		Operations.Add(requestID, op)

		// send the request
		go sendMessage(invokeRequest)

		// wait to be unblocked
		result := <-op.GetChannel()
		switch s := result.(type) {
		case error:
			Logger.Error("signal failed with error",
				zap.String("Signal", signalName),
				zap.Int64("ClientId", clientID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("ContextId", contextID),
				zap.Int64("RequestId", requestID),
				zap.Error(s))

		case bool:
			Logger.Info("signal completed successfully",
				zap.String("Signal", signalName),
				zap.Int64("ClientId", clientID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("ContextId", contextID),
				zap.Int64("RequestId", requestID),
				zap.Bool("Success", s))

		default:
			Logger.Error("signal result unexpected",
				zap.String("Signal", signalName),
				zap.Int64("ClientId", clientID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("ContextId", contextID),
				zap.Int64("RequestId", requestID),
				zap.Any("Result", s))
		}
	})

	// Subscribe to named signal
	workflow.Go(ctx, func(ctx workflow.Context) {
		var err error
		var done bool
		selector = selector.AddReceive(ctx.Done(), func(c workflow.ReceiveChannel, more bool) {
			err = ctx.Err()
			done = true
		})

		// keep select spinning,
		// looking for requests
		for {
			selector.Select(ctx)
			if err != nil {
				Logger.Error("Error In Workflow Context", zap.Error(err))
			}
			if done {
				return
			}
		}
	})

	return reply
}

func handleWorkflowSignalRequest(requestCtx context.Context, request *messages.WorkflowSignalRequest) messages.IProxyReply {
	workflowID := *request.GetWorkflowID()
	runID := *request.GetRunID()
	clientID := request.GetClientID()
	signalName := *request.GetSignalName()
	Logger.Debug("WorkflowSignalRequest Received",
		zap.String("Signal", signalName),
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.String("WorkflowId", workflowID),
		zap.String("RunId", runID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowSignalReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context to signal the workflow
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// signal the specified workflow
	err := clientHelper.SignalWorkflow(
		ctx,
		workflowID,
		runID,
		*request.GetNamespace(),
		signalName,
		request.GetSignalArgs())

	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil)

	return reply
}

func handleWorkflowHasLastResultRequest(requestCtx context.Context, request *messages.WorkflowHasLastResultRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	Logger.Debug("WorkflowHasLastResultRequest Received",
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowHasLastResultReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set ReplayStatus
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)
	reply.Build(nil, workflow.HasLastCompletionResult(ctx))

	return reply
}

func handleWorkflowGetLastResultRequest(requestCtx context.Context, request *messages.WorkflowGetLastResultRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	Logger.Debug("WorkflowGetLastResultRequest Received",
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowGetLastResultReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set replay status
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)

	// get the last completion result from the temporal client
	var result []byte
	err := workflow.GetLastCompletionResult(ctx, &result)
	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil, result)

	return reply
}

func handleWorkflowDisconnectContextRequest(requestCtx context.Context, request *messages.WorkflowDisconnectContextRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	Logger.Debug("WorkflowDisconnectContextRequest Received",
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowDisconnectContextReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create a new disconnected context
	// and then replace the existing one with the new one
	disconnectedCtx, cancel := workflow.NewDisconnectedContext(wectx.GetContext())
	wectx.SetContext(disconnectedCtx)
	wectx.SetCancelFunction(cancel)

	reply.Build(nil)

	return reply
}

func handleWorkflowGetTimeRequest(requestCtx context.Context, request *messages.WorkflowGetTimeRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	Logger.Debug("WorkflowGetTimeRequest Received",
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowGetTimeReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set replay status
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)

	reply.Build(nil, workflow.Now(ctx))

	return reply
}

func handleWorkflowSleepRequest(requestCtx context.Context, request *messages.WorkflowSleepRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	clientID := request.GetClientID()
	requestID := request.GetRequestID()
	Logger.Debug("WorkflowSleepRequest Received",
		zap.Int64("ClientId", clientID),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", requestID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowSleepReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set ReplayStatus
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)

	// pause the current workflow for the specified duration
	var result interface{}
	future := workflow.NewTimer(ctx, request.GetDuration())

	// wait for the future to be unblocked
	err := future.Get(ctx, &result)
	if err != nil {
		reply.Build(internal.NewTemporalError(err, internal.CanceledError))
		return reply
	}

	reply.Build(nil)

	return reply
}

func handleWorkflowExecuteChildRequest(requestCtx context.Context, request *messages.WorkflowExecuteChildRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	clientID := request.GetClientID()
	requestID := request.GetRequestID()
	workflowName := *request.GetWorkflow()
	Logger.Debug("WorkflowExecuteChildRequest Received",
		zap.String("Workflow", workflowName),
		zap.Int64("ClientId", clientID),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", requestID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowExecuteChildReply
	reply := messages.CreateReplyMessage(request)

	// get the contextID and the corresponding context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// check if replaying
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)

	// set options on the context
	var opts workflow.ChildWorkflowOptions
	if v := request.GetOptions(); v != nil {
		opts = *v
	}

	// set cancellation on the context
	// execute the child workflow
	ctx = workflow.WithChildOptions(ctx, opts)
	ctx = workflow.WithScheduleToStartTimeout(ctx, request.GetScheduleToStartTimeout())
	ctx, cancel := workflow.WithCancel(ctx)
	childFuture := workflow.ExecuteChildWorkflow(ctx, workflowName, request.GetArgs())

	// create the new ChildContext
	// add the ChildWorkflowFuture and the cancel func to the
	// ChildContexts map in the parent workflow's entry
	// in the WorkflowContexts map
	cctx := proxyworkflow.NewChild(childFuture, cancel)
	childID := wectx.AddChild(wectx.NextChildID(), cctx)

	// get the child workflow execution
	childWE := new(workflow.Execution)
	err := childFuture.GetChildWorkflowExecution().Get(ctx, childWE)
	if err != nil {
		reply.Build(err)
	}

	reply.Build(nil, append(make([]interface{}, 0), childID, childWE))

	return reply
}

func handleWorkflowWaitForChildRequest(requestCtx context.Context, request *messages.WorkflowWaitForChildRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	childID := request.GetChildID()
	Logger.Debug("WorkflowWaitForChildRequest Received",
		zap.Int64("ChildId", childID),
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowWaitForChildReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	cctx := wectx.GetChild(childID)
	if cctx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set ReplayStatus
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)

	// wait on the child workflow
	var result []byte
	if err := cctx.GetFuture().Get(ctx, &result); err != nil {
		var temporalError *internal.TemporalError
		if isCanceledErr(err) {
			temporalError = internal.NewTemporalError(err, internal.CanceledError)
		} else {
			temporalError = internal.NewTemporalError(err)
		}

		reply.Build(temporalError)

		return reply
	}

	reply.Build(nil, result)

	defer wectx.RemoveChild(childID)

	return reply
}

func handleWorkflowSignalChildRequest(requestCtx context.Context, request *messages.WorkflowSignalChildRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	childID := request.GetChildID()
	clientID := request.GetClientID()
	requestID := request.GetRequestID()
	workerID := request.GetWorkerID()
	signalName := *request.GetSignalName()
	Logger.Debug("WorkflowSignalChildRequest Received",
		zap.String("Signal", signalName),
		zap.Int64("ChildId", childID),
		zap.Int64("ClientId", clientID),
		zap.Int64("WorkerId", workerID),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", requestID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowSignalChildReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	cctx := wectx.GetChild(childID)
	if cctx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set ReplayStatus
	ctx := wectx.GetContext()
	setReplayStatus(ctx, reply)

	// signal the child workflow
	future := cctx.GetFuture().SignalChildWorkflow(
		ctx,
		signalName,
		request.GetSignalArgs())

	// wait on the future
	var result []byte
	if err := future.Get(ctx, &result); err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil, result)

	return reply
}

func handleWorkflowCancelChildRequest(requestCtx context.Context, request *messages.WorkflowCancelChildRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	childID := request.GetChildID()
	Logger.Debug("WorkflowCancelChildRequest Received",
		zap.Int64("ChildId", childID),
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowCancelChildReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	cctx := wectx.GetChild(childID)
	if cctx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// set replaying
	setReplayStatus(wectx.GetContext(), reply)

	// get cancel function
	// call the cancel function
	cancel := cctx.GetCancelFunction()
	cancel()

	reply.Build(nil)

	return reply
}

func handleWorkflowSetQueryHandlerRequest(requestCtx context.Context, request *messages.WorkflowSetQueryHandlerRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	clientID := request.GetClientID()
	workerID := request.GetWorkerID()
	queryName := *request.GetQueryName()
	Logger.Debug("WorkflowSetQueryHandlerRequest Received",
		zap.String("QueryName", queryName),
		zap.Int64("ClientId", clientID),
		zap.Int64("ContextId", contextID),
		zap.Int64("WorkerId", workerID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowSetQueryHandlerReply
	reply := messages.CreateReplyMessage(request)

	// get the workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// define the handler function
	ctx := wectx.GetContext()
	queryHandler := func(queryArgs []byte) ([]byte, error) {
		requestID := NextRequestID()
		Logger.Debug("Workflow Queried",
			zap.String("Query", queryName),
			zap.Int64("ClientId", clientID),
			zap.Int64("ContextId", contextID),
			zap.Int64("WorkerId", workerID),
			zap.Int64("RequestId", requestID),
			zap.Int("ProcessId", os.Getpid()))

		invokeRequest := messages.NewWorkflowQueryInvokeRequest()
		invokeRequest.SetRequestID(requestID)
		invokeRequest.SetContextID(contextID)
		invokeRequest.SetQueryArgs(queryArgs)
		invokeRequest.SetQueryName(&queryName)
		invokeRequest.SetClientID(clientID)
		invokeRequest.SetWorkerID(workerID)

		// set ReplayStatus
		setReplayStatus(ctx, invokeRequest)

		// create the Operation for this request and add it to the operations map
		op := NewOperation(requestID, invokeRequest)
		op.SetContextID(contextID)
		op.SetChannel(make(chan interface{}))
		Operations.Add(requestID, op)

		// send the request
		go sendMessage(invokeRequest)

		Logger.Debug("WorkflowQueryInvoke sent",
			zap.String("Query", queryName),
			zap.Int64("ClientId", clientID),
			zap.Int64("ContextId", contextID),
			zap.Int64("WorkerId", workerID),
			zap.Int64("RequestId", requestID),
			zap.Int("ProcessId", os.Getpid()))

		// wait for InvokeReply
		result := <-op.GetChannel()
		switch s := result.(type) {
		case error:
			Logger.Error("Query Failed With Error",
				zap.String("Query", queryName),
				zap.Int64("ClientId", clientID),
				zap.Int64("ContextId", contextID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("RequestId", requestID),
				zap.Error(s),
				zap.Int("ProcessId", os.Getpid()))

			return nil, s

		case []byte:
			Logger.Info("Query Completed Successfully",
				zap.String("Query", queryName),
				zap.Int64("ClientId", clientID),
				zap.Int64("ContextId", contextID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("RequestId", requestID),
				zap.ByteString("Result", s),
				zap.Int("ProcessId", os.Getpid()))

			return s, nil

		default:
			Logger.Error("Query result unexpected",
				zap.String("Query", queryName),
				zap.Int64("ClientId", clientID),
				zap.Int64("ContextId", contextID),
				zap.Int64("WorkerId", workerID),
				zap.Int64("RequestId", requestID),
				zap.Any("Result", s),
				zap.Int("ProcessId", os.Getpid()))

			return nil, fmt.Errorf("unexpected result type %v.  result must be an error or []byte", reflect.TypeOf(s))
		}
	}

	// Set the query handler with the
	// temporal server
	err := workflow.SetQueryHandler(ctx, queryName, queryHandler)
	if err != nil {
		reply.Build(err)
		return reply
	}

	reply.Build(nil)

	return reply
}

func handleWorkflowQueryRequest(requestCtx context.Context, request *messages.WorkflowQueryRequest) messages.IProxyReply {
	workflowID := *request.GetWorkflowID()
	runID := *request.GetRunID()
	clientID := request.GetClientID()
	queryName := *request.GetQueryName()
	Logger.Debug("WorkflowQueryRequest Received",
		zap.String("QueryName", queryName),
		zap.Int64("ClientId", clientID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.String("WorkflowId", workflowID),
		zap.String("RunId", runID),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowQueryReply
	reply := messages.CreateReplyMessage(request)

	clientHelper := Clients.Get(clientID)
	if clientHelper == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// create the context
	ctx, cancel := context.WithTimeout(requestCtx, clientHelper.GetClientTimeout())
	defer cancel()

	// query the workflow via the temporal client
	value, err := clientHelper.QueryWorkflow(
		ctx,
		workflowID,
		runID,
		*request.GetNamespace(),
		queryName,
		request.GetQueryArgs())

	if err != nil {
		reply.Build(err)
		return reply
	}

	// extract the result
	var result []byte
	if value.HasValue() {
		err = value.Get(&result)
		if err != nil {
			reply.Build(err)
			return reply
		}
	}

	reply.Build(nil, result)

	return reply
}

func handleWorkflowGetVersionRequest(requestCtx context.Context, request *messages.WorkflowGetVersionRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	Logger.Debug("WorkflowGetVersionRequest Received",
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowGetVersionReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	ctx := wectx.GetContext()

	// set ReplayStatus
	setReplayStatus(ctx, reply)

	// get the workflow version
	version := workflow.GetVersion(
		ctx,
		*request.GetChangeID(),
		workflow.Version(request.GetMinSupported()),
		workflow.Version(request.GetMaxSupported()))

	reply.Build(nil, version)

	return reply
}

func handleWorkflowQueueNewRequest(requestCtx context.Context, request *messages.WorkflowQueueNewRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	queueID := request.GetQueueID()
	Logger.Debug("WorkflowQueueNewRequest Received",
		zap.Int64("QueueId", queueID),
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowQueueNewReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	ctx := wectx.GetContext()

	// set ReplayStatus
	setReplayStatus(ctx, reply)

	capacity := int(request.GetCapacity())
	queue := workflow.NewBufferedChannel(ctx, capacity)
	queueID = wectx.AddQueue(queueID, queue)

	Logger.Info("Queue successfully added",
		zap.Int64("QueueId", queueID),
		zap.Int("Capacity", capacity),
		zap.Int64("ContextId", contextID))

	reply.Build(nil)

	return reply
}

func handleWorkflowQueueWriteRequest(requestCtx context.Context, request *messages.WorkflowQueueWriteRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	queueID := request.GetQueueID()
	Logger.Debug("WorkflowQueueWriteRequest Received",
		zap.Int64("QueueId", queueID),
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowQueueWriteReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	ctx := wectx.GetContext()

	// set ReplayStatus
	setReplayStatus(ctx, reply)

	data := request.GetData()
	queue := wectx.GetQueue(queueID)
	if queue == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// check if we should block and wait to enqueue
	// or just try to enqueue without blocking.
	if request.GetNoBlock() {
		s := workflow.NewSelector(ctx)

		// indicates that the value was successfully added to the queue
		// and is not full.
		s.AddSend(queue, data, func() {
			reply.Build(nil, false)
		})

		// indicates that the queue is full and the value was not added
		// to the queue.
		s.AddDefault(func() {
			reply.Build(nil, true)
		})
		s.Select(ctx)
	} else {
		// send data to queue
		queue.Send(ctx, data)
	}

	Logger.Info("Successfully Added to Queue",
		zap.Int64("QueueId", queueID),
		zap.Any("Data", data),
		zap.Int64("ContextId", contextID))

	reply.Build(nil)

	return reply
}

func handleWorkflowQueueReadRequest(requestCtx context.Context, request *messages.WorkflowQueueReadRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	queueID := request.GetQueueID()
	Logger.Debug("WorkflowQueueReadRequest Received",
		zap.Int64("QueueId", queueID),
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowQueueReadReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)

	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	ctx := wectx.GetContext()

	// set ReplayStatus
	setReplayStatus(ctx, reply)

	queue := wectx.GetQueue(queueID)
	if queue == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// read value from queue
	var data []byte
	var isClosed bool
	var temporalError *internal.TemporalError
	timeout := request.GetTimeout()
	s := workflow.NewSelector(ctx)

	// check for timeout
	if timeout > time.Duration(0) {
		timer := workflow.NewTimer(ctx, timeout)
		s.AddFuture(timer, func(f workflow.Future) {
			isReady := false
			err := f.Get(ctx, &isReady)
			if err != nil {
				temporalError = internal.NewTemporalError(err, internal.CanceledError)
			} else {
				temporalError = internal.NewTemporalError(fmt.Errorf("Timeout reading from workflow queue: %d", queueID), internal.TimeoutError)
			}
		})
	}
	s.AddReceive(queue, func(c workflow.ReceiveChannel, more bool) {
		c.Receive(ctx, &data)
		if data == nil {
			isClosed = true
		}
	})
	s.Select(ctx)

	reply.Build(temporalError, append(make([]interface{}, 0), data, isClosed))

	return reply
}

func handleWorkflowQueueCloseRequest(requestCtx context.Context, request *messages.WorkflowQueueCloseRequest) messages.IProxyReply {
	contextID := request.GetContextID()
	queueID := request.GetQueueID()
	Logger.Debug("WorkflowQueueCloseRequest Received",
		zap.Int64("QueueId", queueID),
		zap.Int64("ClientId", request.GetClientID()),
		zap.Int64("ContextId", contextID),
		zap.Int64("RequestId", request.GetRequestID()),
		zap.Int("ProcessId", os.Getpid()))

	// new WorkflowQueueCloseReply
	reply := messages.CreateReplyMessage(request)

	// get the child context from the parent workflow context
	wectx := WorkflowContexts.Get(contextID)
	if wectx == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	ctx := wectx.GetContext()

	// set ReplayStatus
	setReplayStatus(ctx, reply)

	queue := wectx.GetQueue(queueID)
	if queue == nil {
		reply.Build(internal.ErrEntityNotExist)
		return reply
	}

	// close the queue
	queue.Close()

	Logger.Info("Successfully closed Queue",
		zap.Int64("QueueId", queueID),
		zap.Int64("ContextId", contextID))

	reply.Build(nil)

	return reply
}
