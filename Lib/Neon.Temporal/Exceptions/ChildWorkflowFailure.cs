//-----------------------------------------------------------------------------
// FILE:	    ChildWorkflowFailure.cs
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
    /// Returned from workflow when child workflow returned an error.
    /// Examine inner exception to get the original cause.
    /// </summary>
    public class ChildWorkflowFailure : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="execution">The execution of the child workflow.</param>
        /// <param name="namespace">The namespace in which the workflow is executing.</param>
        /// <param name="workflowType">The <see cref="string"/> workflow type name.</param>
        /// <param name="initiatedEventId">TODO - FIGURE THIS OUT</param>
        /// <param name="startedEventId">TODO - FIGURE THIS OUT</param>
        /// <param name="retryState">The state of retry the workflow is in.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public ChildWorkflowFailure(
            WorkflowExecution execution,
            string     @namespace,
            string     workflowType,
            long       initiatedEventId,
            long       startedEventId,
            RetryState retryState,
            string     message        = null,
            Exception  innerException = null)
            :base (
                 TemporalErrorType.ChildWorkflowExecution,
                 message,
                 innerException)
        {
            Execution        = execution;
            Namespace        = @namespace;
            WorkflowType     = workflowType;
            InitiatedEventId = initiatedEventId;
            StartedEventId   = startedEventId;
            RetryState       = retryState;
        }

        /// <summary>
        /// The execution of the child workflow.
        /// </summary>
        public WorkflowExecution Execution { get; }

        /// <summary>
        /// The namespace in which the workflow is executing.
        /// </summary>
        public string Namespace { get; }

        /// <summary>
        /// The <see cref="string"/> workflow type name.
        /// </summary>
        public string WorkflowType { get; }

        /// <summary>
        /// TODO - FIGURE THIS OUT
        /// </summary>
        public long InitiatedEventId { get; }

        /// <summary>
        /// TODO - FIGURE THIS OUT
        /// </summary>
        public long StartedEventId { get; }

        /// <summary>
        /// The state of retry the workflow is in.
        /// </summary>
        public RetryState RetryState { get; }
    }
}
