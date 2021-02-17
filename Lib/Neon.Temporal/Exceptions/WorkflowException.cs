//-----------------------------------------------------------------------------
// FILE:	    WorkflowTypeException.cs
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

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Thrown when ak workflow interface or implementation is not valid.
    /// </summary>
    public class WorkflowException : TemporalException
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public WorkflowException()
        {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">Optionally specifies an inner exception.</param>
        public WorkflowException(
            string       workflowId,
            string       runId,
            WorkflowType workflowType,
            string       message = null ,
            Exception    innerException = null)
            : base(message, innerException)
        {
            this.WorkflowId   = workflowId;
            this.RunId        = runId;
            this.WorkflowType = workflowType;
        }

        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.WorkflowExecutionError;

        public string WorkflowId { get; }
        public string RunId { get; }
        public WorkflowType WorkflowType { get; }
    }
}
