//-----------------------------------------------------------------------------
// FILE:	    TemporalFailure.cs
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

using Neon.Temporal.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// Adds the idea of an original error property to include the 
    /// underlying cause of an excpetion.
    /// </summary>
    public class TemporalFailure : TemporalException
    {
        private readonly TemporalErrorType temporalErrorType;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="temporalErrorType">The type of error that caused the failure.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public TemporalFailure(
            TemporalErrorType temporalErrorType,
            string            message        = null, 
            Exception         innerException = null)
            : base(message, innerException)
        {
            this.temporalErrorType = temporalErrorType;
        }

        /// <summary>
        /// Returns the message that caused the original failure.
        /// </summary>
        public string OriginalMessage { get; set; }

        internal override TemporalErrorType TemporalErrorType => temporalErrorType;
    }
}
