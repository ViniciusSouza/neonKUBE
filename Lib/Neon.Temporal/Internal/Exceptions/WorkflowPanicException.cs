//-----------------------------------------------------------------------------
// FILE:	    WorkflowPanicException.cs
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
    /// Exception that contains information about panicked workflow.
	/// Used to distinguish go panic in the workflow code from a PanicError returned from a workflow function.
    /// </summary>
    public class WorkflowPanicException : TemporalFailure
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="value">The string value of the panic as json.</param>
        /// <param name="stackTrace">The stack trace of the panic.</param>
        /// <param name="failure">The original failure.</param>
        /// <param name="message">Optionally specifies a message.</param>
        /// <param name="innerException">Optionally specifies the inner exception.</param>
        public WorkflowPanicException(
            string    value,
            string    stackTrace,
            Failure   failure,
            string    message        = null,
            Exception innerException = null)
            : base(failure, message, innerException)
        {
            StackTrace = stackTrace;
            Value      = value;
        }

        /// <summary>
        /// The string value of the panic as json.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// The stack trace of the panic.
        /// </summary>
        public new string StackTrace { get; set; }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        internal override TemporalErrorType TemporalErrorType => TemporalErrorType.Panic;
    }
}
