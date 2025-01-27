﻿//-----------------------------------------------------------------------------
// FILE:	    ActivityHeartbeatTimeoutException.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Thrown when an activity did not send a timely heartbeat to Temporal.
    /// </summary>
    public class ActivityHeartbeatTimeoutException : TemporalTimeoutException
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies an inner exception.</param>
        public ActivityHeartbeatTimeoutException(string message = null, Exception innerException = null)
            : base(message, innerException)
        {
            base.Reason = "cadenceInternal:Timeout HEARTBEAT";
        }

        /// <inheritdoc/>
        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.Timeout;

        /// <inheritdoc/>
        internal override string TemporalError => "ActivityHeartbeatTimeoutError";
    }
}
