//-----------------------------------------------------------------------------
// FILE:	    ExecuteWorkflowParams.cs
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
    /// Describes how to execute a workflow when continued as new.
    /// </summary>
    public class ExecuteWorkflowParams
    {
        /// <summary>
        /// Specifies the workflow options to override when the workflow restarts.
        /// </summary>
        public ContinueAsNewOptions WorkflowOptions {get; set;}

        /// <summary>
        /// Specifies the workflow to restart.
        /// </summary>
        public WorkflowType WorkflowType { get; set; }

        /// <summary>
        /// Specifies the attempt number of workflow restarts.
        /// </summary>
        public int Attempt { get; set; }
    }
}
