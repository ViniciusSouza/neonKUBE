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
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public TemporalFailure(string message = null, Exception innerException = null)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Returns the message that caused the original failure.
        /// </summary>
        public string OriginalMessage => InnerException.Message;
    }
}
