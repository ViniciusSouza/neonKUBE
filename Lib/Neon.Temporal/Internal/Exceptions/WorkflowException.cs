//-----------------------------------------------------------------------------
// FILE:	    WorkflowTypeException.cs
// CONTRIBUTOR: John Burns
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

namespace Neon.Temporal.Internal
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
        /// <param name="execution">The execution that raised the exception.</param>
        /// <param name="workflowType">The type of the workflow that raised the exception.</param>
        /// <param name="cause">The cause of the exception.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public WorkflowException(
            WorkflowExecution execution,
            WorkflowType      workflowType,
            string            cause,
            string            message        = null ,
            Exception         innerException = null)
            : base(message, innerException)
        {
            Cause        = cause;
            WorkflowId   = execution.WorkflowId;
            RunId        = execution.RunId;
            WorkflowType = workflowType;
        }

        /// <summary>
        /// The id of the failed workflow.
        /// </summary>
        public string WorkflowId { get; set; }

        /// <summary>
        /// The run id of the failed workflow.
        /// </summary>
        public string RunId { get; set; }

        /// <summary>
        /// The type of the workflow that raised the exception.
        /// </summary>
        public WorkflowType WorkflowType { get; set; }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.WorkflowExecution;
    }
}
