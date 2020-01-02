﻿//-----------------------------------------------------------------------------
// FILE:	    WorkflowQueueClosedException.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2020 by neonFORGE, LLC.  All rights reserved.
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

using Neon.Cadence.Internal;

namespace Neon.Cadence
{
    /// <summary>
    /// Indicates that a Cadence <see cref="WorkflowQueue{T}.DequeueAsync(TimeSpan)"/> operation
    /// failed because the queue has been closed.
    /// </summary>
    public class WorkflowQueueClosedException : CadenceException
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public WorkflowQueueClosedException(string message = null, Exception innerException = null)
            : base(message, innerException)
        {
        }

        /// <inheritdoc/>
        internal override CadenceErrorTypes CadenceErrorType => CadenceErrorTypes.Custom;

        /// <inheritdoc/>
        internal override string Reason => typeof(WorkflowQueueClosedException).FullName;
    }
}
