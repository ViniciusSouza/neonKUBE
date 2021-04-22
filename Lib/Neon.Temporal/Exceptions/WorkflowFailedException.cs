//-----------------------------------------------------------------------------
// FILE:	    WorkflowFailedExcpetion.cs
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
using System.Collections.Generic;
using System.Text;

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Indicates that a workflow failed.
    /// </summary>
    public class WorkflowFailedException : WorkflowException
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="retryState">The retry state of the failed workflow.</param>
        /// <param name="workflowTask">The task on which the workflow failed.</param>
        /// <param name="execution">The execution that raised the exception.</param>
        /// <param name="workflowType">The type of the workflow that raised the exception.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public WorkflowFailedException(
            RetryState        retryState,
            string            workflowTask,
            WorkflowExecution execution,
            WorkflowType      workflowType,
            string            message        = null,
            Exception         innerException = null)
            : base(
                  execution,
                  workflowType,
                  message,
                  innerException)
        {
            this.RetryState   = retryState;
            this.WorkflowTask = workflowTask;
        }

        /// <summary>
        /// The retry state of the failed workflow.
        /// </summary>
        public RetryState RetryState { get; }

        /// <summary>
        /// The task on which the workflow failed.
        /// </summary>
        public string WorkflowTask { get; }
    }
}
