//-----------------------------------------------------------------------------
// FILE:	    TimeoutType.cs
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
using System.Runtime.Serialization;
using System.Text;

namespace Neon.Temporal
{
    /// <summary>
    /// Enumerates the type of timeout.
    /// Used for Exception handling.
    /// </summary>
    public enum TimeoutType
    {
        /// <summary>
        /// Unspecified.
        /// </summary>
        [EnumMember(Value = "Unsepcified")]
        Unspecified = 0,

        /// <summary>
        /// Start to close timeout.
        /// </summary>
        [EnumMember(Value = "Unsepcified")]
        StartToClose = 1,

        /// <summary>
        /// Schedule to start timeout.
        /// </summary>
        [EnumMember(Value = "Unsepcified")]
        ScheduleToStart = 2,

        /// <summary>
        /// Schedule to close timeout.
        /// </summary>
        [EnumMember(Value = "Unsepcified")]
        ScheduleToClose = 3,

        /// <summary>
        /// Heartbeat timeout.
        /// </summary>
        [EnumMember(Value = "Unsepcified")]
        Heartbeat = 4,

    }
}
