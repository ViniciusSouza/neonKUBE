//-----------------------------------------------------------------------------
// FILE:	    TemporalErrorType.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

using Neon.Common;
using Neon.Temporal;

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// <b>INTERNAL USE ONLY:</b> Enumerates the Temporal error types.
    /// </summary>
    public enum TemporalErrorType
    {
        /// <summary>
        /// A generic error.
        /// </summary>
        [EnumMember(Value = "generic")]
        Generic,

        /// <summary>
        /// Used for non temporal related erros (i.e. Bad Request)
        /// </summary>
        [EnumMember(Value = "custom")]
        Custom,

        /// <summary>
        /// Error returned from activity implementations with message and optional details.
        /// </summary>
        [EnumMember(Value = "application")]
        Application,

        /// <summary>
        /// Error returned when operation was canceled.
        /// </summary>
        [EnumMember(Value = "canceled")]
        Canceled,

        /// <summary>
        /// Error returned from workflow when activity returned an error
        /// </summary>
        [EnumMember(Value = "activity")]
        Activity,

        /// <summary>
        /// Error can be returned from server.
        /// </summary>
        [EnumMember(Value = "server")]
        Server,

        /// <summary>
        /// Error returned from workflow when child workflow returned an error.
        /// </summary>
        [EnumMember(Value = "childWorkflowExecution")]
        ChildWorkflowExecution,

        /// <summary>
        /// Error returned from workflow.
        /// </summary>
        [EnumMember(Value = "workflowExecution")]
        WorkflowExecution,

        /// <summary>
        /// Error returned when activity or child workflow timed out.
        /// </summary>
        [EnumMember(Value = "timeout")]
        Timeout,

        /// <summary>
        /// Error returned when workflow was terminated.
        /// </summary>
        [EnumMember(Value = "terminated")]
        Terminated,

        /// <summary>
        /// Error contains information about panicked workflow/activity.
        /// </summary>
        [EnumMember(Value = "panic")]
        Panic,

        /// <summary>
        /// Error can be returned when external workflow doesn't exist.
        /// </summary>
        [EnumMember(Value = "unknownExternalWorkflowExecution")]
        UnknownExternalWorkflowExecution
    }
}
