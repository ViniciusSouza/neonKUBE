//-----------------------------------------------------------------------------
// FILE:	    ServerFailure.cs
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

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Failures that can be returned from server.
    /// </summary>
    public class ServerFailure : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="isNonRetryable">Indicated if failure is not retryable.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public ServerFailure(
            bool      isNonRetryable,
            string    message        = null, 
            Exception innerException = null)
            :base(
                 TemporalErrorType.Server,
                 message,
                 innerException)
        {
            IsNonRetryable = isNonRetryable;
        }

        /// <summary>
        /// Indicated if failure is not retryable.
        /// </summary>
        public bool IsNonRetryable { get; }
    }
}
