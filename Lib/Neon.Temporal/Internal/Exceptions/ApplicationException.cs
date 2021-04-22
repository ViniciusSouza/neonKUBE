//-----------------------------------------------------------------------------
// FILE:	    ApplicationException.cs
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

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// Represents failures that can cross workflow
    /// and activity boundaries.  Any unhandled exception
    /// thrown by an activity or workflow will be converted
    /// to an instance of <see cref="ApplicationException"/>.
    /// </summary>
    public class ApplicationException : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="errType">The failure type represented as a <see cref="string"/>.</param>
        /// <param name="cause">The cause of the exception.</param>
        /// <param name="nonRetryable">Indicated if failure is not retryable.</param>
        /// <param name="details">Encodes strong typed detail data of the failure.</param>
        /// <param name="failure">The original failure.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public ApplicationException(
            string    errType,
            string    cause,
            bool      nonRetryable,
            byte[]    details,
            Failure   failure,
            string    message        = null,
            Exception innerException = null)
            : base(failure, message, innerException)
        {
            Cause        = cause;
            ErrType      = errType;
            NonRetryable = nonRetryable;
            Details      = details;
        }

        /// <summary>
        /// The failure type represented as a <see cref="string"/>.
        /// </summary>
        public string ErrType { get; set; }

        /// <summary>
        /// Indicated if failure is not retryable.
        /// </summary>
        public bool NonRetryable { get; set; }

        /// <summary>
        /// Extracts strong typed detail data of the application failure.
        /// </summary>
        public byte[] Details { get; set; }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.Application;
    }
}

