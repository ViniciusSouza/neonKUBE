using Neon.Temporal.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace Neon.Temporal
{
    /// <summary>
    /// Adds the idea of an original error property to include the 
    /// underlying cause of an excpetion.
    /// </summary>
    public class TemporalFailure : TemporalException
    {
        private TemporalErrorType temporalErrorType;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public TemporalFailure(
            string            originalMessage = null,
            string            message         = null, 
            TemporalException innerException  = null)
            : base(message, innerException)
        {
            this.OriginalMessage   = originalMessage;
            this.temporalErrorType = innerException.TemporalErrorType;
        }

        internal override TemporalErrorType TemporalErrorType => TemporalErrorType;

        public string OriginalMessage { get; }
    }
}
