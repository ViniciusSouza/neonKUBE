using System;
using System.Collections.Generic;
using System.Text;

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Base class for Temporal activity related exceptions that
    /// will result in the completion of a running activity that
    /// raises one of the derived exceptions.
    /// </summary>
    public class ActivityCompletionException : TemporalException
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
        public ActivityCompletionException(
            string       activityId,
            string       runId,
            string       workflowId,
            ActivityType activityType,
            string       message        = null, 
            Exception    innerException = null)
            : base(message, innerException)
        {
            this.ActivityId   = activityId;
            this.RunId        = runId;
            this.WorkflowId   = workflowId;
            this.ActivityType = activityType;
        }

        /// <inheritdoc/>
        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.Activity;

        internal string ActivityId { get; }
        internal string RunId { get; }
        internal string WorkflowId { get; }
        internal ActivityType ActivityType { get; }
    }
}
