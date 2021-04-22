//-----------------------------------------------------------------------------
// FILE:	    TimeoutException.cs
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
using System.Text;

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// Exception returned when activity or child workflow timed out.
    /// </summary>
    public class TimeoutException : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="timeoutType">Specifies the type of timeout.</param>
        /// <param name="lastHeartbeatDetails">Encodes details of the last heartbeat before timeout.</param>
        /// <param name="cause">The cause of the error.</param>
        /// <param name="failure">The original failure.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public TimeoutException(
            TimeoutType timeoutType,
            byte[]      lastHeartbeatDetails,
            string      cause,
            Failure     failure,
            string      message        = null,
            Exception   innerException = null)
            : base (failure, message, innerException)
        {
            Cause               = cause;
            TimeoutType         = timeoutType;
            LastHearbeatDetails = lastHeartbeatDetails;
        }

        /// <summary>
        /// Specifies the type of timeout.
        /// </summary>
        public TimeoutType TimeoutType { get; set;  }

        /// <summary>
        /// Encodes details of the last heartbeat before timeout.
        /// </summary>
        public byte[] LastHearbeatDetails { get; set;  }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.Timeout;
    }
}
