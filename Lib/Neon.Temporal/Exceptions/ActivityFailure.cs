using System;
using System.Collections.Generic;
using System.Text;

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    public class ActivityFailure : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public ActivityFailure(
            string       activityId,
            ActivityType activityType,
            RetryState   retryState,
            long         scheduledEventId,
            long         startedEventId,
            string       identity       = null,
            string       message        = null, 
            Exception    innerException = null)
            : base(message, innerException)
        {
        }
    }
}
