﻿//-----------------------------------------------------------------------------
// FILE:	    ChildWorkflowOptions.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
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
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reflection;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;

namespace Neon.Cadence
{
    /// <summary>
    /// Specifies the options to use when executing a child workflow.
    /// </summary>
    public class ChildWorkflowOptions
    {
        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// <b>INTERNAL USE ONLY:</b> Normalizes the options passed by creating or cloning a new 
        /// instance as required and filling unset properties using default client settings.
        /// </summary>
        /// <param name="client">The associated Cadence client.</param>
        /// <param name="options">The input options or <c>null</c>.</param>
        /// <param name="workflowInterface">Optionally specifies the workflow interface definition.</param>
        /// <returns>The normalized options.</returns>
        /// <exception cref="ArgumentNullException">Thrown if a valid task list is not specified.</exception>
        public static ChildWorkflowOptions Normalize(CadenceClient client, ChildWorkflowOptions options, Type workflowInterface = null)
        {
            Covenant.Requires<ArgumentNullException>(client != null, nameof(client));

            if (options == null)
            {
                options = new ChildWorkflowOptions();
            }
            else
            {
                options = options.Clone();
            }

            if (!options.ScheduleToCloseTimeout.HasValue || options.ScheduleToCloseTimeout.Value <= TimeSpan.Zero)
            {
                options.ScheduleToCloseTimeout = client.Settings.WorkflowScheduleToCloseTimeout;
            }

            if (!options.ScheduleToStartTimeout.HasValue || options.ScheduleToStartTimeout.Value <= TimeSpan.Zero)
            {
                options.ScheduleToStartTimeout = client.Settings.WorkflowScheduleToStartTimeout;
            }

            if (!options.TaskStartToCloseTimeout.HasValue || options.TaskStartToCloseTimeout.Value <= TimeSpan.Zero)
            {
                options.TaskStartToCloseTimeout = client.Settings.WorkflowTaskStartToCloseTimeout;
            }

            if (!options.WorkflowIdReusePolicy.HasValue)
            {
                options.WorkflowIdReusePolicy = Cadence.WorkflowIdReusePolicy.AllowDuplicateFailedOnly;
            }

            if (string.IsNullOrEmpty(options.TaskList))
            {
                if (workflowInterface != null)
                {
                    CadenceHelper.ValidateWorkflowInterface(workflowInterface);

                    var interfaceAttribute = workflowInterface.GetCustomAttribute<WorkflowInterfaceAttribute>();

                    if (interfaceAttribute != null && !string.IsNullOrEmpty(interfaceAttribute.TaskList))
                    {
                        options.TaskList = interfaceAttribute.TaskList;
                    }
                }
            }

            if (string.IsNullOrEmpty(options.TaskList))
            {
                throw new ArgumentNullException("You must specify a valid task list explicitly or via an [WorkflowInterface(TaskList = \"my-tasklist\")] attribute on the target workflow interface.");
            }

            return options;
        }

        //---------------------------------------------------------------------
        // Instance members

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ChildWorkflowOptions()
        {
        }

        /// <summary>
        /// Specifies the task list where the child workflow will be scheduled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A task list must be specified when executing a workflow.  For workflows
        /// started via a typed stub, this will default to the type list specified
        /// by the <c>[WorkflowInterface(TaskList = "my-tasklist"]</c> tagging the
        /// interface (if any).
        /// </para>
        /// <para>
        /// For workflow stubs created from an interface without a specified task list
        /// or workflows created via untyped or external stubs, this will need to
        /// be explicitly set to a non-empty value.
        /// </para>
        /// </remarks>
        public string Domain { get; set; } = null;

        /// <summary>
        /// Optionally specifies the workflow ID to assign to the child workflow.
        /// A UUID will be generated by default.
        /// </summary>
        public string WorkflowId { get; set; } = null;

        /// <summary>
        /// Optionally specifies the task list where the child workflow will be
        /// scheduled.  This defaults to the parent's task list.
        /// </summary>
        public string TaskList { get; set; } = null;

        /// <summary>
        /// Specifies the maximum time the child workflow may execute from start
        /// to finish.  This defaults to <see cref="CadenceSettings.WorkflowScheduleToCloseTimeoutSeconds"/>.
        /// </summary>
        public TimeSpan? ScheduleToCloseTimeout { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Optionally specifies the default maximum time a workflow can wait between being scheduled
        /// and actually begin executing.  This defaults to <c>24 hours</c>.
        /// </summary>
        public TimeSpan? ScheduleToStartTimeout { get; set; }

        /// <summary>
        /// Optionally specifies the decision task timeout for the child workflow.
        /// This defaults to <see cref="CadenceSettings.WorkflowTaskStartToCloseTimeout"/>.
        /// </summary>
        public TimeSpan? TaskStartToCloseTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Optionally specifies what happens to the child workflow when the parent is terminated.
        /// This defaults to <see cref="ChildPolicy.Abandon"/>.
        /// </summary>
        public ChildPolicy ChildPolicy { get; set; } = ChildPolicy.Abandon;

        /// <summary>
        /// Optionally specifies whether to wait for the child workflow to finish for any
        /// reason including being: completed, failed, timedout, terminated, or canceled.
        /// </summary>
        public bool WaitUntilFinished { get; set; } = false;

        /// <summary>
        /// Controls how Cadence handles workflows that attempt to reuse workflow IDs.
        /// This defaults to <see cref="WorkflowIdReusePolicy.AllowDuplicateFailedOnly"/>.
        /// </summary>
        public WorkflowIdReusePolicy? WorkflowIdReusePolicy { get; set; }

        /// <summary>
        /// Optionally specifies retry options.
        /// </summary>
        public RetryOptions RetryOptions { get; set; } = null;

        /// <summary>
        /// Optionally specifies a recurring schedule for the workflow.  This can be set to a string specifying
        /// the minute, hour, day of month, month, and day of week scheduling parameters using the standard Linux
        /// CRON format described here: <a href="https://en.wikipedia.org/wiki/Cron">https://en.wikipedia.org/wiki/Cron</a>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cadence accepts a CRON string formatted as a single line of text with 5 parameters separated by
        /// spaces.  The parameters specified the minute, hour, day of month, month, and day of week values:
        /// </para>
        /// <code>
        /// ┌───────────── minute (0 - 59)
        /// │ ┌───────────── hour (0 - 23)
        /// │ │ ┌───────────── day of the month (1 - 31)
        /// │ │ │ ┌───────────── month (1 - 12)
        /// │ │ │ │ ┌───────────── day of the week (0 - 6) (Sunday to Saturday)
        /// │ │ │ │ │
        /// │ │ │ │ │
        /// * * * * * 
        /// </code>
        /// <para>
        /// Each parameter may be set to one of:
        /// </para>
        /// <list type="table">
        /// <item>
        ///     <term><b>*</b></term>
        ///     <description>
        ///     Matches any value.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>value</b></term>
        ///     <description>
        ///     Matches a specific integer value.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>first-last</b></term>
        ///     <description>
        ///     Matches a range of integer values (inclusive).
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>value1,value2,...</b></term>
        ///     <description>
        ///     Matches a list of integer values.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>first/step</b></term>
        ///     <description>
        ///     Matches values starting at <b>first</b> and then succeeding incremented by <b>step</b>.
        ///     </description>
        /// </item>
        /// </list>
        /// <para>
        /// You can use this handy CRON calculator to see how this works: <a href="https://crontab.guru">https://crontab.guru</a>
        /// </para>
        /// </remarks>
        public string CronSchedule { get; set; }

        /// <summary>
        /// Converts this instance into the corresponding internal object.
        /// </summary>
        /// <returns>The equivalent <see cref="InternalChildWorkflowOptions"/>.</returns>
        internal InternalChildWorkflowOptions ToInternal()
        {
            return new InternalChildWorkflowOptions()
            {
                Domain                       = this.Domain,
                ChildPolicy                  = (int)this.ChildPolicy,
                CronSchedule                 = this.CronSchedule,
                ExecutionStartToCloseTimeout = CadenceHelper.ToCadence(this.ScheduleToCloseTimeout.Value),
                RetryPolicy                  = this.RetryOptions?.ToInternal(),
                TaskList                     = this.TaskList,
                TaskStartToCloseTimeout      = CadenceHelper.ToCadence(this.TaskStartToCloseTimeout.Value),
                WaitForCancellation          = this.WaitUntilFinished,
                WorkflowID                   = this.WorkflowId,
                WorkflowIdReusePolicy        = (int)(this.WorkflowIdReusePolicy ?? Cadence.WorkflowIdReusePolicy.AllowDuplicateFailedOnly)
            };
        }

        /// <summary>
        /// Returns a shallow clone of the current instance.
        /// </summary>
        /// <returns>The cloned <see cref="WorkflowOptions"/>.</returns>
        public ChildWorkflowOptions Clone()
        {
            return new ChildWorkflowOptions()
            {
                Domain                       = this.Domain,
                CronSchedule                 = this.CronSchedule,
                ChildPolicy                  = this.ChildPolicy,
                ScheduleToCloseTimeout       = this.ScheduleToCloseTimeout,
                RetryOptions                 = this.RetryOptions,
                ScheduleToStartTimeout       = this.ScheduleToStartTimeout,
                TaskList                     = this.TaskList,
                TaskStartToCloseTimeout      = this.TaskStartToCloseTimeout,
                WaitUntilFinished            = this.WaitUntilFinished,
                WorkflowId                   = this.WorkflowId,
                WorkflowIdReusePolicy        = this.WorkflowIdReusePolicy
            };
        }
    }
}
