using System;
using System.Collections.Generic;
using System.Text;

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Temporal Activity exception that usually indicates 
    /// that activity was already completed (duplicated request to complete) 
    /// or timed out or workflow is closed.
    /// </summary>
    public class ActivityCanceledException : ActivityCompletionException
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="activityId"></param>
        /// <param name="runId"></param>
        /// <param name="workflowId"></param>
        /// <param name="activityType"></param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public ActivityCanceledException(
            string       activityId,
            string       runId,
            string       workflowId,
            ActivityType activityType,
            string       message        = null,
            Exception    innerException = null)
            : base(
                  activityId,
                  runId,
                  workflowId,
                  activityType,
                  message,
                  innerException)
        {
        }
    }
}
