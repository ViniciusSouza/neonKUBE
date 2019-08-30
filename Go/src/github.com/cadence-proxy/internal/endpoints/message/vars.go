//-----------------------------------------------------------------------------
// FILE:		vars.go
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

package message

import (
	"net/http"
	"sync"

	proxyactivity "github.com/cadence-proxy/internal/cadence/activity"
	proxyclient "github.com/cadence-proxy/internal/cadence/client"
	proxyworker "github.com/cadence-proxy/internal/cadence/worker"
	proxyworkflow "github.com/cadence-proxy/internal/cadence/workflow"
)

var (
	mu sync.RWMutex

	// requestID is incremented (protected by a mutex) every time
	// a new request message is sent
	requestID int64

	// httpClient is the HTTP client used to send requests
	// to the Neon.Cadence client
	httpClient = http.Client{}

	// terminate is a boolean that will be set after handling an incoming
	// TerminateRequest.  A true value will indicate that the server instance
	// needs to gracefully shut down after handling the request, and a false value
	// indicates the server continues to run
	terminate bool

	// ActivityContexts maps a int64 ContextId to the cadence
	// Activity Context passed to the cadence Activity functions.
	// The cadence-client will use contextIds to refer to specific
	// activity contexts when perfoming activity actions
	ActivityContexts = new(proxyactivity.ActivityContextsMap)

	// Workers maps a int64 WorkerId to the cadence
	// Worker returned by the Cadence NewWorker() function.
	// This will be used to stop a worker via the
	// StopWorkerRequest.
	Workers = new(proxyworker.WorkersMap)

	// WorkflowContexts maps a int64 ContextId to the cadence
	// Workflow Context passed to the cadence Workflow functions.
	// The cadence-client will use contextIds to refer to specific
	// workflow ocntexts when perfoming workflow actions
	WorkflowContexts = new(proxyworkflow.WorkflowContextsMap)

	// Operations is a map of operations used to track pending
	// cadence-client operations
	Operations = new(operationsMap)

	// Clients is a map of ClientHelpers to ClientID used to
	// store ClientHelpers to support multiple clients
	Clients = new(proxyclient.ClientsMap)
)

//----------------------------------------------------------------------------
// RequestID thread-safe methods

// NextRequestID increments the package variable
// requestID by 1 and is protected by a mutex lock
func NextRequestID() int64 {
	mu.Lock()
	requestID = requestID + 1
	defer mu.Unlock()
	return requestID
}

// GetRequestID gets the value of the global variable
// requestID and is protected by a mutex Read lock
func GetRequestID() int64 {
	mu.RLock()
	defer mu.RUnlock()
	return requestID
}