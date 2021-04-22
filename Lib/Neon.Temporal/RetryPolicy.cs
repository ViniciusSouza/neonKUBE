﻿//-----------------------------------------------------------------------------
// FILE:	    RetryOptions.cs
// CONTRIBUTOR: Jeff Lill
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
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;

using Newtonsoft.Json;

using Neon.Common;
using Neon.Retry;
using Neon.Temporal;
using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Describes a Temporal retry policy.
    /// </summary>
    public class RetryPolicy
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public RetryPolicy()
        {
        }

        /// <summary>
        /// Constructs an instance from a <see cref="LinearRetryPolicy"/>.
        /// </summary>
        /// <param name="policy">The policy.</param>
        public RetryPolicy(LinearRetryPolicy policy)
        {
            Covenant.Requires<ArgumentNullException>(policy != null, nameof(policy));

            this.InitialInterval    = TemporalHelper.Normalize(policy.RetryInterval);
            this.BackoffCoefficient = 1.0;

            if (policy.Timeout.HasValue)
            {
                this.ExpirationInterval = TemporalHelper.Normalize(policy.Timeout.Value);
            }

            this.MaximumAttempts = policy.MaxAttempts;
        }

        /// <summary>
        /// Constructs an instance from a <see cref="ExponentialRetryPolicy"/>,
        /// </summary>
        /// <param name="policy">The policy.</param>
        public RetryPolicy(ExponentialRetryPolicy policy)
        {
            Covenant.Requires<ArgumentNullException>(policy != null, nameof(policy));

            this.InitialInterval    = TemporalHelper.Normalize(policy.InitialRetryInterval);
            this.BackoffCoefficient = 2.0;
            this.MaximumInterval    = TemporalHelper.Normalize(policy.MaxRetryInterval);

            if (policy.Timeout.HasValue)
            {
                this.ExpirationInterval = TemporalHelper.Normalize(policy.Timeout.Value);
            }

            this.MaximumAttempts = policy.MaxAttempts;
        }

        /// <summary>
        /// Specifies the backoff interval for the first retry.  If coefficient is 1.0 then
        /// it is used for all retries.  Required, no default value.
        /// </summary>
        [JsonConverter(typeof(GoTimeSpanJsonConverter))]
        public TimeSpan InitialInterval { get; set; }

        /// <summary>
        /// Specifies the coefficient used to calculate the next retry backoff interval.  
        /// The next retry interval is previous interval multiplied by this coefficient. 
        /// This must be 1 or larger. Default is 2.0.
        /// </summary>
        public double BackoffCoefficient { get; set; } = 2.0;

        /// <summary>
        /// Specifies the maximim retry interval.  Retries intervals will start at <see cref="InitialInterval"/>
        /// and then be multiplied by <see cref="BackoffCoefficient"/> for each retry attempt until the
        /// interval reaches or exceeds <see cref="MaximumInterval"/>, at which point point each
        /// retry will use <see cref="MaximumInterval"/> for all subsequent attempts.
        /// </summary>
        [JsonConverter(typeof(GoTimeSpanJsonConverter))]
        public TimeSpan MaximumInterval { get; set; }

        /// <summary>
        /// Maximum time to retry.  Either <see cref="ExpirationInterval"/> or <see cref="MaximumAttempts"/> is 
        /// required.  Retries will stop when this is exceeded even if maximum retries is not been reached.
        /// </summary>
        [JsonConverter(typeof(GoTimeSpanJsonConverter))]
        public TimeSpan ExpirationInterval { get; set; }

        /// <summary>
        /// Maximum number of attempts.  When exceeded the retries stop.  If not set or set to 0, it means 
        /// unlimited, and the policy will rely on <see cref="ExpirationInterval"/> to decide when to stop
        /// retrying.  Either <see cref="MaximumAttempts"/> or <see cref="MaximumInterval"/>"/> is required.
        /// </summary>
        public int MaximumAttempts { get; set; }

        // $todo(jefflill):
        //
        // We'd align better with the Java client if this was a list of [TemporalException] derived
        // exceptions rather than GOLANG error strings.  We'll revisit this when the port is further
        // along.

        /// <summary>
        /// <para>
        /// Specifies Temporal errors that <b>should not</b> trigger a retry. This is optional.  Temporal server 
        /// will stop retrying if error reason matches this list.  Use the <see cref="Temporal.NonRetriableErrors"/>
        /// class methods to initialize this list as required.
        /// </para>
        /// <note>
        /// Cancellation is not a failure, so that won't be retried.
        /// </note>
        /// </summary>
        /// <remarks>
        /// <para>
        /// You can specify non-retryable error reasons directly here or use the <see cref="DoNotRetry(string)"/> method
        /// to append a specific reason string or <see cref="DoNotRetry{ExceptionType}()"/> to specify the error reason for an
        /// exception type.
        /// </para>
        /// <para>
        /// We recommend that you use <see cref="DoNotRetry{ExceptionType}()"/> most of the time and reserve
        /// <see cref="DoNotRetry(string)"/> for interop situations where you need to integrate 
        /// with something written in another language.
        /// </para>
        /// <para>
        /// For native Temporal exceptions like <see cref="TemporalTimeoutException"/>, <see cref="DoNotRetry{ExceptionType}()"/> is smart
        /// enough to append the proper error reason.  For other exception types, this method will use the fully qualified
        /// exception type name as the reason.
        /// </para>
        /// </remarks>
        public List<string> NonRetriableErrors { get; set; } = new List<string>();

        /// <summary>
        /// <para>
        /// Appends the error reason corresponding to an exception type.  For built-in Temporal exceptions, this
        /// will append the apporpriate reason string and for other exception type, this will append the fully
        /// qualified exception type name.
        /// </para>
        /// <note>
        /// We generally recommend that you use <see cref="DoNotRetry{ExceptionType}()"/> by default and
        /// reserve this for situations where you need to specify a specific reason, probably for interoperating
        /// with workflows and activities written in other lanagues, etc.
        /// </note>
        /// </summary>
        /// <typeparam name="ExceptionType">The exception type.</typeparam>
        public void DoNotRetry<ExceptionType>()
            where ExceptionType : Exception
        {
            var type = typeof(ExceptionType);

            if (type.Inherits<TemporalException>())
            {
                // We need to construct an instance to discover the associated error reason.

                var temporalException = Activator.CreateInstance<ExceptionType>() as TemporalException;

                NonRetriableErrors.Add(temporalException.Cause);
            }
            else
            {
                NonRetriableErrors.Add(type.FullName);
            }
        }

        /// <summary>
        /// <para>
        /// Appends the string passed to <see cref="NonRetriableErrors"/> as a reason not to be retried.
        /// </para>
        /// <note>
        /// We generally recommend that you use <see cref="DoNotRetry{ExceptionType}()"/> by default and
        /// reserve this for situations where you need to specify a specific reason, probably for interoperating
        /// with workflows and activities written in other lanagues, etc.
        /// </note>
        /// </summary>
        /// <param name="reason">The reason string.</param>
        public void DoNotRetry(string reason)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(reason), nameof(reason));

            NonRetriableErrors.Add(reason);
        }
    }
}
