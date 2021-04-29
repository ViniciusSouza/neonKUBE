﻿//-----------------------------------------------------------------------------
// FILE:	    ContinueAsNewOptions.cs
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Neon.Common;
using Neon.Temporal;

namespace Neon.Temporal
{
    /// <summary>
    /// Specifies the options to be used when continuing a workflow as a 
    /// new instance.
    /// </summary>
    public class ContinueAsNewOptions
    {
        /// <summary>
        /// Optionally overrides the current workflow's execution timeout for the restarted
        /// workflow when this value is greater than <see cref="TimeSpan.Zero"/>.
        /// </summary>
        [JsonConverter(typeof(GoTimeSpanJsonConverter))]
        public TimeSpan WorkflowExecutionTimeout { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's run timeout for the restarted
        /// workflow when this value is greater than <see cref="TimeSpan.Zero"/>.
        /// </summary>
        [JsonConverter(typeof(GoTimeSpanJsonConverter))]
        public TimeSpan WorkflowRunTimeout { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's timeout for the restarted
        /// workflow when this value is greater than <see cref="TimeSpan.Zero"/>.
        /// </summary>
        [JsonConverter(typeof(GoTimeSpanJsonConverter))]
        public TimeSpan WorkflowTaskTimeout { get; set; }

        /// <summary>
        /// Optionally overrides the name of the workflow to continue as new.
        /// </summary>
        public string Workflow { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's Id when restarting.
        /// </summary>
        public string WorkflowId { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's task queue when restarting.
        /// </summary>
        [JsonProperty(PropertyName = "TaskQueueName")]
        public string TaskQueue { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's namespace when restarting.
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's retry options when restarting.
        /// </summary>
        public RetryPolicy RetryPolicy { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's wait for cancellation property when restarting.
        /// </summary>
        public bool WaitForCancellation { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's cron schedule when restarting.
        /// </summary>
        public string CronSchedule { get; set; }

        /// <summary>
        /// Optionally overrides the current workflow's id reuse policy when restarting
        /// </summary>
        public WorkflowIdReusePolicy WorkflowIdReusePolicy { get; set; }
    }
}
