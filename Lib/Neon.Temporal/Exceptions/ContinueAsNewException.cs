//-----------------------------------------------------------------------------
// FILE:	    ContinueAsNewException.cs
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

using Neon.Temporal.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace Neon.Temporal
{
    /// <summary>
    /// Contains information about how to continue the workflow as new.
    /// </summary>
    public class ContinueAsNewException : Exception
    {
        /// <summary>
        /// Constructs an instance using explicit arguments.
        /// </summary>
        /// <param name="args">Optional arguments for the new execution.</param>
        /// <param name="workflowId">Optional id of the new execution.</param>
        /// <param name="workflow">Optional workflow for the new execution.</param>
        /// <param name="namespace">Optional domain for the new execution.</param>
        /// <param name="taskQueue">Optional task list for the new execution.</param>
        /// <param name="cronSchedule">Optional cron schedule for the new workflow.</param>
        /// <param name="waitForCancellation">Optional wait for cancelation flag for the new execution.</param>
        /// <param name="attempt">Optional attempt count of the new execution.</param>
        /// <param name="workflowExecutionTimeout">Optional workflow execution timeout for the new execution.</param>
        /// <param name="workflowRunTimeout">Optional workflow run timeout for the new execution.</param>
        /// <param name="workflowTaskTimeout">Optional schedule to start timeout for the new execution.</param>
        /// <param name="reusePolicy">Optional workflow id reuse policy for the new execution.</param>
        /// <param name="retryPolicy">Optional retry options for the new execution.</param>
        public ContinueAsNewException(
            byte[] args                       = null,
            string workflowId                 = null,
            string workflow                   = null,
            string @namespace                 = null,
            string taskQueue                  = null,
            string cronSchedule               = null,
            bool waitForCancellation          = false,
            int attempt                       = default,
            TimeSpan workflowExecutionTimeout = default,
            TimeSpan workflowRunTimeout       = default,
            TimeSpan workflowTaskTimeout      = default,
            WorkflowIdReusePolicy reusePolicy = default,
            RetryPolicy retryPolicy           = default)
        {
            
            Args            = args;
            Workflow        = workflow;
            ExecutionParams = new ExecuteWorkflowParams
            {
                Attempt         = attempt,
                WorkflowType    = new WorkflowType { Name = workflow },
                WorkflowOptions = new ContinueAsNewOptions
                {
                    TaskQueue                = taskQueue,
                    WorkflowExecutionTimeout = workflowExecutionTimeout,
                    WorkflowRunTimeout       = workflowRunTimeout,
                    WorkflowTaskTimeout      = workflowTaskTimeout,
                    Namespace                = @namespace,
                    WorkflowId               = workflowId,
                    WaitForCancellation      = waitForCancellation,
                    WorkflowIdReusePolicy    = reusePolicy,
                    CronSchedule             = cronSchedule,
                    RetryPolicy              = retryPolicy
                }
            };
        }

        /// <summary>
        /// Constructs an instance using a <see cref="ContinueAsNewOptions"/>.
        /// </summary>
        /// <param name="args">Arguments for the new execution (this may be <c>null)</c>).</param>
        /// <param name="options">Options for the new execution  (this may be <c>null</c>).</param>
        public ContinueAsNewException(byte[] args, ContinueAsNewOptions options)
        {
            this.Args = args;

            if (options != null)
            {
                ExecutionParams = new ExecuteWorkflowParams
                {
                    WorkflowType    = new WorkflowType { Name = options.Workflow },
                    WorkflowOptions = options,
                };
            }
        }

        /// <summary>
        /// Specifies the workflow to restart.
        /// </summary>
        public string Workflow { get; private set; }

        /// <summary>
        /// The parameters that describe how the workflow will restart.
        /// </summary>
        public ExecuteWorkflowParams ExecutionParams { get; private set; }

        /// <summary>
        /// The workflow arguments.
        /// </summary>
        public byte[] Args { get; set; }
    }
}
