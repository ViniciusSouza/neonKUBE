//-----------------------------------------------------------------------------
// FILE:		helper.go
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

package proxyclient

import (
	"context"
	"errors"
	"strings"
	"sync"
	"time"

	"go.temporal.io/temporal-proto/workflowservice"
	"go.temporal.io/temporal/client"
	"go.temporal.io/temporal/encoded"
	"go.temporal.io/temporal/worker"
	"go.temporal.io/temporal/workflow"
	"go.uber.org/zap"
	"google.golang.org/grpc"

	proxyerror "temporal-proxy/internal/temporal/error"
)

const (

	// _namespaceNotExistsErrorStr is the string message of the error thrown by
	// temporal when trying to perform and operation on a namespace that does not
	// yet exists.
	_namespaceNotExistErrorStr = "EntityNotExistsError{Message: Namespace:"
)

type (

	// ClientHelper holds configuration details for building
	// the temporal namespace client and the temporal workflow client
	// This is used for creating, update, and registering temporal namespaces
	// and stoping/starting temporal workflows workers.
	//
	// Contains:
	//	- TemporalClientConfiguration -> configuration information for building the temporal workflow and namespace clients.
	//	- *zap.Logger -> reference to a zap.Logger to log temporal client output to the console.
	//	- *TemporalClientBuilder -> reference to a TemporalClientBuilder used to build the temporal namespace and workflow clients.
	// 	- client.NamespaceClient -> temporal namespace client instance used to interact with Temporal namespaces.
	// 	- *WorkfloClientsMap -> a thread-safe map of temporal workflow client instance mapped to their respective namespaces.
	// 	- time.Duration -> specifies the amount of time in seconds a reply has to be sent after a request has been received by the temporal-proxy.
	ClientHelper struct {
		serviceClient   workflowservice.WorkflowServiceClient
		clientOptions   client.Options
		Logger          *zap.Logger
		Builder         *TemporalClientBuilder
		NamespaceClient client.NamespaceClient
		WorkflowClients *WorkflowClientsMap
	}

	// WorkflowClientsMap holds a thread-safe map[interface{}]interface{} of
	// temporal WorkflowClients with their namespace.
	WorkflowClientsMap struct {
		sync.Mutex
		clients map[string]client.Client
	}
)

// NewClientHelper is the default constructor
// for a new ClientHelper.
//
// returns *ClientHelper -> pointer to a newly created ClientHelper.
func NewClientHelper() *ClientHelper {
	helper := new(ClientHelper)
	helper.WorkflowClients = NewWorkflowClientsMap()
	return helper
}

//----------------------------------------------------------------------------------
// ClientHelper instance methods

// SetHostPort sets the hostPort in a ClientHelper.
//
// param value string --> the string value to set as the hostPort.
func (helper *ClientHelper) SetHostPort(value string) {
	helper.clientOptions.HostPort = value
}

// SetNamespace sets the namespace in a ClientHelper.
//
// params value string -> the string value to set as the namespace.
func (helper *ClientHelper) SetNamespace(value string) {
	helper.clientOptions.Namespace = value
}

// SetClientOptions sets the client.Options of a ClientHelper.
//
// param value client.Options -> client.Options to set.
func (helper *ClientHelper) SetClientOptions(value client.Options) {
	helper.clientOptions = value
}

// SetupServiceConfig configures a ClientHelper's workflowserviceclient.Interface
// Service.  It also sets the Logger, the TemporalClientBuilder, and acts as a helper for
// creating new temporal workflow and namespace clients.
//
// params ctx context.Context -> go context to use to verify a connection has been established to the temporal server.
//
// returns error -> error if there were any problems configuring
// or building the service client.
func (helper *ClientHelper) SetupServiceConfig(ctx context.Context) error {
	if helper.serviceClient != nil {
		return nil
	}

	// Configure the ClientHelper.Builder

	helper.Builder = NewBuilder(helper.Logger).
		SetClientOptions(helper.clientOptions)

	n := 30
	var err error
	var service workflowservice.WorkflowServiceClient

	// build the service client
	// retry n number of times

	for i := 0; i <= n; i++ {
		service, err = helper.Builder.BuildServiceClient()
		if err != nil {
			time.Sleep(time.Second * 1)
			continue
		}
		break
	}

	if err != nil {
		helper.Logger.Error("Failed to build Temporal workflow service client", zap.Error(err))
		helper = nil
		return err
	}

	helper.serviceClient = service

	// build namespace client

	namespaceClient, err := helper.Builder.BuildTemporalNamespaceClient()
	if err != nil {
		helper.Logger.Error("failed to build namespace temporal client.", zap.Error(err))
		return err
	}

	helper.NamespaceClient = namespaceClient

	// validate that a connection has been established
	// make a channel that waits for a connection to be established
	// until returning ready

	connectChan := make(chan error)
	defer close(connectChan)

	// poll on system namespace

	err = helper.pollNamespace(ctx, connectChan, client.DefaultNamespace)
	if err != nil {
		helper = nil
		return err
	}

	// build the workflow client

	workflowClient, err := helper.Builder.BuildClient()
	if err != nil {
		helper.Logger.Error("failed to build namespace temporal client.", zap.Error(err))
		return nil
	}

	_ = helper.WorkflowClients.Add(helper.Builder.GetNamespace(), workflowClient)

	return nil
}

// SetupTemporalClients establishes a connection to a running temporal server
// instance and configures namespace and workflow clients.
//
// params:
// 	- ctx context.Context -> the context used to poll the server to see if a
// 	connection has been established.
// 	- opts client.Options -> the client options for connection the the temporal
// 	server instance.
//
// returns error -> error if any errors are thrown while trying to establish a
// connection, or nil upon success.
func (helper *ClientHelper) SetupTemporalClients(ctx context.Context, opts client.Options) error {
	helper.SetClientOptions(opts)
	if err := helper.SetupServiceConfig(ctx); err != nil {
		defer func() {
			helper = nil
		}()
		return err
	}
	return nil
}

// StartWorker starts a workflow worker and activity worker based on configured options.
// The worker will listen for workflows registered with the same taskList.
//
// params:
//	- namespace string -> the namespace that identifies the client to start the worker with.
// 	- taskList string -> the name of the group of temporal workflows for the worker to listen for.
// 	- options worker.Options -> Options used to configure a worker instance.
//	- workerID int64 -> the id of the new worker that will be mapped internally in
// 	the temporal-proxy.
//
// returns:
//	- worker.Worker -> the worker.Worker returned by the worker.New()
// 	call to the temporal server.
// 	- returns error -> an error if the workflow could not be started, or nil if
// 	the workflow was triggered successfully.
func (helper *ClientHelper) StartWorker(
	namespace string,
	taskList string,
	options worker.Options,
) (worker.Worker, error) {
	client, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return nil, err
	}

	worker := worker.New(client, taskList, options)
	if worker.Start() != nil {
		return nil, err
	}

	return worker, nil
}

// StopWorker stops a worker at the given workerID.
//
// param worker.Worker -> the worker to be stopped.
func (helper *ClientHelper) StopWorker(worker worker.Worker) {
	worker.Stop()
}

// DescribeNamespace gets the description of a registered temporal namespace.
//
// params:
//	- ctx context.Context -> the context to use to execute the describe namespace
// 	request to temporal.
//	- namespace string -> the namespace you want to query.
//
// returns:
//	- *workflowservice.DescribeNamespaceResponse -> response to the describe namespace request.
// 	- error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) DescribeNamespace(ctx context.Context, namespace string) (*workflowservice.DescribeNamespaceResponse, error) {
	resp, err := helper.NamespaceClient.Describe(ctx, namespace)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("Namespace Describe Response", zap.Any("Namespace Info", *resp.NamespaceInfo))

	return resp, nil
}

// RegisterNamespace registers a temporal namespace.
//
// params:
//	- ctx context.Context -> the context to use to execute the Register namespace
// 	request to temporal.
// 	- request *workflowservice.RegisterNamespaceRequest -> the request
// 	to register the temporal namespace.
//
// returns error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) RegisterNamespace(ctx context.Context, request *workflowservice.RegisterNamespaceRequest) error {
	err := helper.NamespaceClient.Register(ctx, request)
	if err != nil {
		return err
	}

	helper.Logger.Info("namespace successfully registered", zap.String("Namespace Name", request.GetName()))

	return nil
}

// UpdateNamespace updates a temporal namespace.
//
// params:
//	- ctx context.Context -> the context to use to execute the Update namespace
// 	request to temporal.
// 	- request *workflowservice.UpdateNamespaceRequest -> the request
// 	to Update the temporal namespace.
//
// returns error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) UpdateNamespace(ctx context.Context, request *workflowservice.UpdateNamespaceRequest) error {
	err := helper.NamespaceClient.Update(ctx, request)
	if err != nil {
		return err
	}

	helper.Logger.Info("namespace successfully updated", zap.String("Namespace Name", request.GetName()))

	return nil
}

// ListNamespaces lists information about the temporal namespaces.
//
// params:
//	- ctx context.Context -> the context to use to execute the describe namespace
// 	request to temporal.
// 	- request *workflowservice.ListNamespacesRequest -> the *workflowservice.ListNamespacesRequest to
// 	query temporal for a list of namespaces.
//	- opts ...grpc.CallOptions
//
// returns:
//	- *workflowservice.ListNamespacesResponse -> response to the describe task list request.
// 	- error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) ListNamespaces(
	ctx context.Context,
	request *workflowservice.ListNamespacesRequest,
	opts ...grpc.CallOption,
) (*workflowservice.ListNamespacesResponse, error) {
	resp, err := helper.serviceClient.ListNamespaces(ctx, request, opts...)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("List namespaces response", zap.Any("Namespaces", resp.Namespaces))

	return resp, nil
}

// ExecuteWorkflow execute a registered temporal workflow.
//
// params:
//	- ctx context.Context -> the context to use to execute the workflow.
// 	- namespace string -> the namespace to start the workflow on.
// 	- options client.StartWorkflowOptions -> configuration parameters for starting a workflow execution.
// 	- workflow interface{} -> a registered temporal workflow.
// 	- args ...interface{} -> anonymous number of arguments for starting a workflow.
//
// returns:
//	- client.WorkflowRun -> the client.WorkflowRun returned by the workflow execution
// 	call to the temporal server.
// 	- error -> an error if the workflow could not be started, or nil if
// 	the workflow was triggered successfully.
func (helper *ClientHelper) ExecuteWorkflow(
	ctx context.Context,
	namespace string,
	options client.StartWorkflowOptions,
	workflow interface{},
	args ...interface{},
) (client.WorkflowRun, error) {
	n := 30
	var workflowRun client.WorkflowRun
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)

	// start the workflow, but put in a loop
	// to check if the namespace has been detected yet
	// by temporal server (primarily for unit testing,
	// loop should never execute more than once in production)
	for i := 0; i < n; i++ {
		workflowRun, err = workflowClient.ExecuteWorkflow(ctx, options, workflow, args...)
		if err != nil {
			if (strings.Contains(err.Error(), _namespaceNotExistErrorStr)) && (i < n-1) {
				time.Sleep(time.Second)
				continue
			}

			return nil, err
		}
		break
	}

	helper.Logger.Info("Started Workflow",
		zap.String("WorkflowID", workflowRun.GetID()),
		zap.String("RunID", workflowRun.GetRunID()))

	return workflowRun, nil
}

// GetWorkflow get a WorkflowRun from existing temporal workflow.
//
// params:
//	- ctx context.Context -> the context to use to get the workflow.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running workflow.
// 	- namespace string -> the namespace the workflow is executing on.
//
// returns:
//	- client.WorkflowRun -> the client.WorkflowRun returned by the GetWorkflow
// 	call to the temporal server.
// 	- error -> an error if the workflow could not be started, or nil if
// 	the workflow was triggered successfully.
func (helper *ClientHelper) GetWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	namespace string,
) (client.WorkflowRun, error) {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return nil, err
	}

	workflowRun := workflowClient.GetWorkflow(ctx, workflowID, runID)

	helper.Logger.Info("Get Workflow",
		zap.String("WorkflowID", workflowRun.GetID()),
		zap.String("RunID", workflowRun.GetRunID()))

	return workflowRun, nil
}

// DescribeTaskList gets the description of a registered temporal namespace.
//
// params:
//	- ctx context.Context -> the context to use to execute the describe namespace
// 	request to temporal.
// 	- request *workflowservice.DescribeTaskListRequest -> the *workflowservice.DescribeTaskListRequest to
// 	query temporal for a task list.
// 	- opts ...grpc.CallOptions -> optional grpc.CallOption.
//
// returns:
//	- *temporalshared.DescribeTaskListResponse -> response to the describe task list request.
// 	request
// 	- error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) DescribeTaskList(
	ctx context.Context,
	request *workflowservice.DescribeTaskListRequest,
	opts ...grpc.CallOption,
) (*workflowservice.DescribeTaskListResponse, error) {
	resp, err := helper.serviceClient.DescribeTaskList(ctx, request, opts...)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("TaskList Describe Response", zap.String("TaskList Info", resp.String()))

	return resp, nil
}

// CancelWorkflow cancel running temporal workflow.
//
// params:
//	- ctx context.Context -> the context to use to cancel the workflow.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running workflow.
// 	- namespace string -> the namespace the workflow is executing on.
//
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was cancelled successfully.
func (helper *ClientHelper) CancelWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	namespace string,
) error {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {

	}
	err = workflowClient.CancelWorkflow(ctx, workflowID, runID)
	if err != nil {
		return err
	}

	helper.Logger.Info("Workflow Cancelled",
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return nil
}

// TerminateWorkflow terminate a running temporal workflow.
//
// params:
//	- ctx context.Context -> the context to use to terminate the workflow.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running workflow.
// 	- namespace string -> the namespace the workflow is executing on.
// 	- reason string -> the string reason for terminating.
// 	- details []byte -> termination details encoded as a []byte.
//
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was terminated successfully.
func (helper *ClientHelper) TerminateWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	namespace string,
	reason string,
	details []byte,
) error {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return err
	}

	err = workflowClient.TerminateWorkflow(
		ctx,
		workflowID,
		runID,
		reason,
		details)

	if err != nil {
		return err
	}

	helper.Logger.Info("Workflow Terminated",
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return nil
}

// SignalWithStartWorkflow signal a temporal workflow to start.
//
// params:
//	- ctx context.Context -> the context to use to get the workflow.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- namespace string -> the namespace the workflow is executing on.
// 	- signalName string -> name of the signal to signal channel to signal the workflow.
// 	- signalArg []byte -> the signalling arguments encoded as a []byte.
// 	- signalOpts client.StartWorkflowOptions -> client.StartWorkflowOptions
// used to start the workflow.
// 	- workflow string -> the name of the workflow to start.
// 	- args ...interface{} -> the optional arguments for starting the workflow.
//
// returns:
//	- *workflow.Execution -> pointer to the resulting workflow execution from
// 	starting the workflow.
// 	- error -> error upon failure and nil upon success.
func (helper *ClientHelper) SignalWithStartWorkflow(
	ctx context.Context,
	workflowID string,
	namespace string,
	signalName string,
	signalArg []byte,
	opts client.StartWorkflowOptions,
	workflow string,
	args ...interface{},
) (*workflow.Execution, error) {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return nil, err
	}

	workflowExecution, err := workflowClient.SignalWithStartWorkflow(
		ctx,
		workflowID,
		signalName,
		signalArg,
		opts,
		workflow,
		args...)

	if err != nil {
		return nil, err
	}

	helper.Logger.Info("Started Workflow",
		zap.String("Workflow", workflow),
		zap.String("WorkflowID", workflowExecution.ID),
		zap.String("RunID", workflowExecution.RunID))

	return workflowExecution, nil
}

// DescribeWorkflowExecution describe the execution
// of a running temporal workflow.
//
// param:
//	- ctx context.Context -> the context to use to cancel the workflow.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running workflow.
// 	- namespace string -> the namespace the workflow is executing on.
//
// returns:
//	- *workflowservice.DescribeWorkflowExecutionResponse -> the response to the
// 	describe workflow execution request.
// 	- error -> an error if the workflow could not be started, or nil if
// 	the workflow was cancelled successfully.
func (helper *ClientHelper) DescribeWorkflowExecution(
	ctx context.Context,
	workflowID string,
	runID string,
	namespace string,
) (*workflowservice.DescribeWorkflowExecutionResponse, error) {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return nil, err
	}

	response, err := workflowClient.DescribeWorkflowExecution(ctx, workflowID, runID)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("Workflow Describe Execution Successful", zap.Any("Execution Info", *response.WorkflowExecutionInfo))

	return response, nil
}

// SignalWorkflow signal a temporal workflow.
//
// params:
//	- ctx context.Context -> the context to use to get the workflow.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running temporal workflow.
// 	- namespace string -> the namespace the workflow is executing on.
// 	- signalName string -> name of the signal to signal channel to signal the workflow.
// 	- arg interface{} -> the signaling arguments.
//
// returns error -> error upon failure and nil upon success.
func (helper *ClientHelper) SignalWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	namespace string,
	signalName string,
	arg interface{},
) error {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return err
	}

	err = workflowClient.SignalWorkflow(
		ctx,
		workflowID,
		runID,
		signalName,
		arg)
	if err != nil {
		return err
	}

	helper.Logger.Info("Successfully Signaled Workflow",
		zap.String("SignalName", signalName),
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return nil
}

// QueryWorkflow query a temporal workflow.
//
// params:
//	-  ctx context.Context -> the context to use to get the workflow.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running temporal workflow.
// 	- namespace string -> the namespace the workflow is executing on.
// 	- queryType string -> name of the query to query channel to query the workflow.
// 	- args ...interface{} -> the optional querying arguments.
//
// returns:
//	- encoded.Value -> the encoded result value of querying a workflow.
// 	- error -> error upon failure and nil upon success.
func (helper *ClientHelper) QueryWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	namespace string,
	queryType string,
	args ...interface{},
) (encoded.Value, error) {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return nil, err
	}

	value, err := workflowClient.QueryWorkflow(
		ctx,
		workflowID,
		runID,
		queryType,
		args...)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("Successfully Queried Workflow",
		zap.String("QueryType", queryType),
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return value, nil
}

// CompleteActivity externally completes the execution of an activity using a
// task token.
//
// params:
//	- ctx context.Context -> the go context used to execute the complete activity call.
// 	- taskToken []byte -> a task token used to complete the activity encoded as
// 	a []byte.
// 	- namespace string -> the namespace the workflow is executing on.
// 	- result interface{} -> the result to complete the activity with.
// 	pararm temporalError *proxyerror.TemporalError -> error to complete the activity with.
//
// returns error -> error upon failure to complete the activity, nil upon success.
func (helper *ClientHelper) CompleteActivity(
	ctx context.Context,
	taskToken []byte,
	namespace string,
	result interface{},
	temporalError *proxyerror.TemporalError,
) error {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return err
	}

	var e error
	if temporalError != nil {
		e = errors.New(temporalError.ToString())
	}

	err = workflowClient.CompleteActivity(ctx, taskToken, result, e)
	if err != nil {
		return err
	}

	helper.Logger.Info("Successfully Completed Activity",
		zap.Any("Result", result),
		zap.Error(e))

	return nil
}

// CompleteActivityByID externally completes the execution of an activity by
// string Id.
//
// params:
//	- ctx context.Context -> the go context used to execute the complete activity call.
// 	- namespace string -> the namespace the activity to complete is running on.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running temporal workflow.
// 	- activityID string -> the activityID of the executing activity to complete.
// 	- result interface{} -> the result to complete the activity with.
// 	- temporalError *proxyerror.TemporalError -> error to complete the activity with.
//
// returns error -> error upon failure to complete the activity, nil upon success.
func (helper *ClientHelper) CompleteActivityByID(
	ctx context.Context,
	namespace string,
	workflowID string,
	runID string,
	activityID string,
	result interface{},
	temporalError *proxyerror.TemporalError,
) error {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return err
	}

	var e error
	if temporalError != nil {
		e = errors.New(temporalError.ToString())
	}

	err = workflowClient.CompleteActivityByID(
		ctx,
		namespace,
		workflowID,
		runID,
		activityID,
		result,
		e)
	if err != nil {
		return err
	}

	helper.Logger.Info("Successfully Completed Activity",
		zap.String("ActivityId", activityID),
		zap.Any("Result", result),
		zap.Error(e))

	return nil
}

// RecordActivityHeartbeat records heartbeat for an activity.
//
// params:
//	- ctx context.Context -> the go context used to record a heartbeat for an activity.
// 	- taskToken []byte -> a task token used to record a heartbeat for an activity
// 	a []byte.
// 	- namespace string -> the namespace the workflow is executing on.
// 	- details ...interface{} -> optional activity heartbeat details.
//
// returns error -> error upon failure to record activity heartbeat, nil upon success.
func (helper *ClientHelper) RecordActivityHeartbeat(
	ctx context.Context,
	taskToken []byte,
	namespace string,
	details ...interface{},
) error {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return err
	}

	err = workflowClient.RecordActivityHeartbeat(ctx, taskToken, details)
	if err != nil {
		return err
	}

	helper.Logger.Info("Successfully Recorded Activity Heartbeat", zap.Any("Details", details))

	return nil
}

// RecordActivityHeartbeatByID records heartbeat for an activity externally by
// string Id.
//
// params:
//	- ctx context.Context -> the go context used to record a heartbeat for an activity.
// 	- namespace string -> the namespace the activity to is running in.
// 	- workflowID string -> the workflowID of the running workflow.
// 	- runID string -> the runID of the running temporal workflow.
// 	- activityID string -> the activityID of the executing activity.
// 	- details ...interface{} -> optional activity heartbeat details.
//
// returns error -> error upon failure to record activity heartbeat, nil upon success.
func (helper *ClientHelper) RecordActivityHeartbeatByID(
	ctx context.Context,
	namespace string,
	workflowID string,
	runID string,
	activityID string,
	details ...interface{},
) error {
	workflowClient, err := helper.GetOrCreateWorkflowClient(namespace)
	if err != nil {
		return err
	}

	err = workflowClient.RecordActivityHeartbeatByID(
		ctx,
		namespace,
		workflowID,
		runID,
		activityID,
		details)
	if err != nil {
		return err
	}

	helper.Logger.Info("Successfully Recorded Activity Heartbeat",
		zap.String("ActivityId", activityID),
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID),
		zap.Any("Details", details))

	return nil
}

// GetOrCreateWorkflowClient queries workflowClients looking for
// a temporal WorkflowClient at a specified namespace.
//
// param namespace string -> the namespace of the temporal WorkflowClient.
//
// returns client.Client -> the WorkflowClient associated with
// the specified namespace.
func (helper *ClientHelper) GetOrCreateWorkflowClient(namespace string) (client.Client, error) {
	wc := helper.WorkflowClients.Get(namespace)
	if wc == nil {
		wc, err := client.NewClient(helper.Builder.clientOptions)
		if err != nil {
			return nil, err
		}
		_ = helper.WorkflowClients.Add(namespace, wc)
	}

	return wc, nil
}

// pollNamespace polls the temporal server to check and see if a connection
// has been established by the service client by polling a namespace.
//
// param ctx context.Context -> context to execute the namespace describe call on.
// param channel chan error -> channel to send error over upon a connection
// failure or nil if a connection was verified.
// param namespace string -> the namespace to query for a connection.
//
// returns error -> error if establishing a connection failed and nil
// upon success.
func (helper *ClientHelper) pollNamespace(
	ctx context.Context,
	channel chan error,
	namespace string,
) error {
	go func() {
		var err error
		defer func() {
			channel <- err
		}()

		_, err = helper.DescribeNamespace(ctx, namespace)
	}()

	// block and catch the result
	if err := <-channel; err != nil {
		return err
	}

	return nil
}

//----------------------------------------------------------------------------
// WorkflowClientsMap instance methods

// NewWorkflowClientsMap is the constructor for an WorkflowClientsMap
func NewWorkflowClientsMap() *WorkflowClientsMap {
	o := new(WorkflowClientsMap)
	o.clients = make(map[string]client.Client)
	return o
}

// Add adds a new temporal WorkflowClient and its corresponding namespace into
// the WorkflowClientsMap map.  This method is thread-safe.
//
// param namespace string -> the namespace for the temporal WorkflowClient.
// This will be the mapped key.
// param wc client.Client -> temporal WorkflowClient used to
// execute workflow functions. This will be the mapped value.
//
// returns string -> the namespace for the temporal WorkflowClient added to the map.
func (wcm *WorkflowClientsMap) Add(namespace string, wc client.Client) string {
	wcm.Lock()
	defer wcm.Unlock()
	wcm.clients[namespace] = wc
	return namespace
}

// Remove removes key/value entry from the WorkflowClientsMap map at the specified
// ContextId.  This is a thread-safe method.
//
// param namespace string -> the namespace for the temporal WorkflowClient.
// This will be the mapped key.
//
// returns string -> the namespace for the temporal WorkflowClient removed from the map.
func (wcm *WorkflowClientsMap) Remove(namespace string) string {
	wcm.Lock()
	defer wcm.Unlock()
	delete(wcm.clients, namespace)
	return namespace
}

// Get gets a WorkflowContext from the WorkflowClientsMap at the specified
// ContextID.  This method is thread-safe.
//
// param namespace string -> the namespace for the temporal WorkflowClient.
// This will be the mapped key.
//
// returns client.Client -> pointer to temporal WorkflowClient with the specified namespace.
func (wcm *WorkflowClientsMap) Get(namespace string) client.Client {
	wcm.Lock()
	defer wcm.Unlock()
	return wcm.clients[namespace]
}
