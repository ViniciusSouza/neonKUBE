﻿//-----------------------------------------------------------------------------
// FILE:	    BadRequestException.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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

using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Thrown when a Temporal receives an invalid request.
    /// </summary>
    public class BadRequestException : TemporalException
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies an inner exception.</param>
        public BadRequestException(string message = null, Exception innerException = null)
            : base(message, innerException)
        {
        }

        /// <inheritdoc/>
        internal override string TemporalError => "BadRequestError";

        /// <inheritdoc/>
        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.Custom;
    }
}
