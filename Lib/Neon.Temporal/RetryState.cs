//-----------------------------------------------------------------------------
// FILE:	    ActivityFailure.cs
// CONTRIBUTOR: Jack Burns
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
using System.Runtime.Serialization;
using System.Text;

namespace Neon.Temporal
{
    /// <summary>
    /// Enumerates and describes the various states a temporal activity
    /// or workflow can exist in with relation to retries
    /// and retry policies.
    /// </summary>
    public enum RetryState
    {
        /// <summary>
        /// RetryState is unspecified.
        /// </summary>
        [EnumMember(Value = "Unspecified")]
        Unspecified = 0,

        /// <summary>
        /// Workflow/activity retry in progress.
        /// </summary>
        [EnumMember(Value = "InProgress")]
        InProgress,

        /// <summary>
        /// A non retryable failure has occurred.
        /// </summary>
        [EnumMember(Value = "NonRetryableFailure")]
        NonRetryableFailure,

        /// <summary>
        /// Workflow/activity has timed out.
        /// </summary>
        [EnumMember(Value = "Timeout")]
        Timeout,

        /// <summary>
        /// Maximum retry attempts has been reached.
        /// </summary>
        [EnumMember(Value = "MaximumAttemptsReached")]
        MaximumAttemptsReached,

        /// <summary>
        /// There is no retry policy set.
        /// </summary>
        [EnumMember(Value = "RetryPolicyNotSet")]
        RetryPolicyNotSet,

        /// <summary>
        /// An internal server error has occurred
        /// affecting the retry state of the workflow/activity.
        /// </summary>
        [EnumMember(Value = "InternalServerError")]
        InternalServerError,

        /// <summary>
        /// Cancelation of the workflow/activity has been requested.
        /// </summary>
        [EnumMember(Value = "CancelRequested")]
        CancelRequested
    }
}
