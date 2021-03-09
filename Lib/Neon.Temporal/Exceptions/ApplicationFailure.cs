//-----------------------------------------------------------------------------
// FILE:	    ApplicationFailure.cs
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
    /// Represents failures that can cross workflow
    /// and activity boundaries.  Any unhandled exception
    /// thrown by an activity or workflow will be converted
    /// to an instance of <see cref="ApplicationFailure"/>.
    /// </summary>
    public class ApplicationFailure : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="type">The failure type represented as a <see cref="string"/>.</param>
        /// <param name="isNonRetryable">Indicated if failure is not retryable.</param>
        /// <param name="newFailure">New ApplicationFailure with isNonRetryable() flag set to false.</param>
        /// <param name="newNonRetryableFailure">New ApplicationFailure with isNonRetryable() flag set to true.</param>
        /// <param name="details">Encodes strong typed detail data of the failure.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public ApplicationFailure(
            string             type,
            bool               isNonRetryable,
            ApplicationFailure newFailure,
            ApplicationFailure newNonRetryableFailure,
            byte[]             details,
            string             message        = null,
            Exception          innerException = null)
            : base(
                    TemporalErrorType.Application,
                    message,
                    innerException)
        {
            Type                   = type;
            IsNonRetryable         = isNonRetryable;
            NewFailure             = newFailure;
            NewNonRetryableFailure = newNonRetryableFailure;
            Details                = details;
            
        }

        /// <summary>
        /// The failure type represented as a <see cref="string"/>.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Indicated if failure is not retryable.
        /// </summary>
        public bool IsNonRetryable { get; }

        /// <summary>
        /// New ApplicationFailure with isNonRetryable() flag set to false.
        /// </summary>
        public ApplicationFailure NewFailure { get; }

        /// <summary>
        /// New ApplicationFailure with isNonRetryable() flag set to true.
        /// </summary>
        public ApplicationFailure NewNonRetryableFailure { get; }

        /// <summary>
        /// Extracts strong typed detail data of the application failure.
        /// </summary>
        public byte[] Details { get; }
    }
}

