using System;
using System.Collections.Generic;
using System.Text;

using Neon.Temporal.Internal;

namespace Neon.Temporal.Exceptions
{
    /// <summary>
    /// Temporal activity exception that indicates
    /// an unexpected failure when completing an activity.
    /// </summary>
    public class ActivityCompletionFailureException : ActivityCompletionException
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
        public ActivityCompletionFailureException (
            string activityId,
            string runId,
            string workflowId,
            ActivityType activityType,
            string message = null,
            Exception innerException = null)
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
