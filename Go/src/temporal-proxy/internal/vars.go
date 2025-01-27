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

package internal

import (
	"go.uber.org/zap/zapcore"
)

const (

	// ContentType is the content type to be used for HTTP requests
	// encapsulationg a ProxyMessage
	ContentType = "application/x-neon-temporal-proxy"

	// TemporalLoggerName is the name of the zap.Logger that will
	// log internal temporal messages.
	TemporalLoggerName = "temporal      "

	// ProxyLoggerName is the name of the zap.Logger that will
	// log internal temporal-proxy messages.
	ProxyLoggerName = "temporal-proxy"
)

var (

	// DebugPrelaunched INTERNAL USE ONLY: Optionally indicates that the temporal-proxy will
	// already be running for debugging purposes.  When this is true, the
	// temporal-client be hardcoded to listen on 127.0.0.2:5001 and
	// the temporal-proxy will be assumed to be listening on 127.0.0.2:5000.
	// This defaults to false.
	DebugPrelaunched = false

	// Debug indicates that the proxy is running in Debug mode.  This
	// is used to configure specified settings.
	Debug = false

	// LogLevel specifies the global LogLevel for the temporal-proxy.
	LogLevel zapcore.LevelEnabler
)
