//-----------------------------------------------------------------------------
// FILE:	    ActivityFailure.cs
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
    /// Returned from workflow when activity throws an exception.
    /// Implements <see cref="TemporalFailure"/> that can be
    /// unwrapped to get the actual cause of the failure.
    /// </summary>
    public class ActivityFailure : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="activityId">Specifies an id of the activity.</param>
        /// <param name="identity">Specifies an identify of the activity.</param>
        /// <param name="activityType">Specifies the type of the activity.</param>
        /// <param name="retryState">Specifies the retry state of the activity.</param>
        /// <param name="scheduledEventId">Specifies the scheduled event id of the activity.</param>
        /// <param name="startedEventId">Specifies the started event id of the activity.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public ActivityFailure(
            string       activityId,
            string       identity,
            ActivityType activityType,
            RetryState   retryState,
            long         scheduledEventId,
            long         startedEventId,
            string       message        = null, 
            Exception    innerException = null)
            : base(
                    TemporalErrorType.Activity,
                    message,
                    innerException)
        {
            ActivityId       = activityId;
            Identity         = identity;
            RetryState       = retryState;
            ScheduledEventId = scheduledEventId;
            StartedEventId   = startedEventId;
            ActivityType     = activityType;
        }

        /// <summary>
        /// The <see cref="string"/> id of the activity.
        /// </summary>
        public string ActivityId { get; }

        /// <summary>
        /// The <see cref="string"/> identity of the activity.
        /// </summary>
        public string Identity { get; }

        /// <summary>
        /// The <see cref="Temporal.RetryState"/> of the activity.
        /// This will reflect the retry behavior of the activity on
        /// failure.
        /// </summary>
        public RetryState RetryState { get; }

        /// <summary>
        /// TODO JACK: Figure out what this is.
        /// </summary>
        public long ScheduledEventId { get; }

        /// <summary>
        /// TODO JACK: Figure this out.
        /// </summary>
        public long StartedEventId { get; }

        /// <summary>
        /// The <see cref="Internal.ActivityType"/> type of the activity.
        /// </summary>
        public ActivityType ActivityType { get; }
    }
}
