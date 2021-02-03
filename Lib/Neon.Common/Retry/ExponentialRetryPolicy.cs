﻿//-----------------------------------------------------------------------------
// FILE:	    ExponentialRetryPolicy.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.
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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Diagnostics;

namespace Neon.Retry
{
    /// <summary>
    /// Implements an <see cref="IRetryPolicy"/> that retries an operation 
    /// first at an initial interval and then doubles the interval up to a limit
    /// for a specified maximum number of times.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You can enable transient error logging by passing a non-empty <b>logCategory</b>
    /// name to the constructor.  This creates an embedded <see cref="INeonLogger"/>
    /// using that name and any retried transient errors will then be logged as
    /// warnings including <b>[transient-retry]</b> in the message.
    /// </para>
    /// <note>
    /// Only the retried errors will be logged.  The final exception thrown after
    /// all retries fail will not be logged because it's assumed that these will
    /// be caught and handled upstack by application code.
    /// </note>
    /// <para>
    /// Choose a category name that can be used to easily identify the affected
    /// component.  For example, <b>couchbase:my-cluster</b> to identify a
    /// specific Couchbase cluster.
    /// </para>
    /// </remarks>
    public class ExponentialRetryPolicy : RetryPolicyBase, IRetryPolicy
    {
        private Func<Exception, bool> transientDetector;

        /// <summary>
        /// Constructs the retry policy with a specific transitent detection function.
        /// </summary>
        /// <param name="transientDetector">
        /// Optionally specifies the function that determines whether an exception is transient 
        /// (see <see cref="TransientDetector"/>).  You can pass <c>null</c>
        /// if all exceptions are to be considered to be transient.
        /// </param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="initialRetryInterval">Optionally specifies the initial retry interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="maxRetryInterval">Optionally specifies the maximum retry interval (defaults to essentially unlimited: 24 hours).</param>
        /// <param name="timeout">Optionally specifies the maximum time the operation will be retried (defaults to unconstrained)</param>
        /// <param name="sourceModule">Optionally enables transient error logging by identifying the source module (defaults to <c>null</c>).</param>
        public ExponentialRetryPolicy(Func<Exception, bool> transientDetector = null, int maxAttempts = 5, TimeSpan? initialRetryInterval = null, TimeSpan? maxRetryInterval = null, TimeSpan? timeout = null, string sourceModule = null)
            : base(sourceModule, timeout)
        {
            Covenant.Requires<ArgumentException>(maxAttempts > 0, nameof(maxAttempts));
            Covenant.Requires<ArgumentException>(initialRetryInterval == null || initialRetryInterval > TimeSpan.Zero, nameof(initialRetryInterval));
            Covenant.Requires<ArgumentNullException>(maxRetryInterval >= initialRetryInterval || initialRetryInterval > TimeSpan.Zero || maxRetryInterval == null, nameof(maxRetryInterval));

            this.transientDetector = transientDetector ?? (e => true);
            this.MaxAttempts = maxAttempts;
            this.InitialRetryInterval = initialRetryInterval ?? TimeSpan.FromSeconds(1);
            this.MaxRetryInterval = maxRetryInterval ?? TimeSpan.FromHours(24);

            if (InitialRetryInterval > MaxRetryInterval)
            {
                InitialRetryInterval = MaxRetryInterval;
            }
        }

        /// <summary>
        /// Constructs the retry policy to handle a specific exception type as transient.
        /// </summary>
        /// <param name="exceptionType">The exception type to be considered to be transient.</param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="initialRetryInterval">Optionally specifies the initial retry interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="maxRetryInterval">Optionally specifies the maximum retry interval (defaults to essentially unlimited: 24 hours).</param>
        /// <param name="timeout">Optionally specifies the maximum time the operation will be retried (defaults to unconstrained)</param>
        /// <param name="sourceModule">Optionally enables transient error logging by identifying the source module (defaults to <c>null</c>).</param>
        public ExponentialRetryPolicy(Type exceptionType, int maxAttempts = 5, TimeSpan? initialRetryInterval = null, TimeSpan? maxRetryInterval = null, TimeSpan? timeout = null, string sourceModule = null)
            : this
            (
                e => TransientDetector.MatchException(e, exceptionType),
                maxAttempts,
                initialRetryInterval,
                maxRetryInterval,
                timeout,
                sourceModule
            )
        {
            Covenant.Requires<ArgumentNullException>(exceptionType != null, nameof(exceptionType));
        }

        /// <summary>
        /// Constructs the retry policy to handle a multiple exception types as transient.
        /// </summary>
        /// <param name="exceptionTypes">The exception type to be considered to be transient.</param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="initialRetryInterval">Optionally specifies the initial retry interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="maxRetryInterval">Optionally specifies the maximum retry interval (defaults to essentially unlimited: 24 hours).</param>
        /// <param name="timeout">Optionally specifies the maximum time the operation will be retried (defaults to unconstrained)</param>
        /// <param name="sourceModule">Optionally enables transient error logging by identifying the source module (defaults to <c>null</c>).</param>
        public ExponentialRetryPolicy(Type[] exceptionTypes, int maxAttempts = 5, TimeSpan? initialRetryInterval = null, TimeSpan? maxRetryInterval = null, TimeSpan? timeout = null, string sourceModule = null)
            : this
            (
                e =>
                {
                    if (exceptionTypes == null)
                    {
                        return false;
                    }

                    foreach (var type in exceptionTypes)
                    {
                        if (TransientDetector.MatchException(e, type))
                        {
                            return true;
                        }
                    }

                    return false;
                },
                maxAttempts,
                initialRetryInterval,
                maxRetryInterval,
                timeout,
                sourceModule
            )
        {
        }

        /// <summary>
        /// Returns the maximum number of times the action should be attempted.
        /// </summary>
        public int MaxAttempts { get; private set; }

        /// <summary>
        /// Returns the initial interval between action retry attempts.
        /// </summary>
        public TimeSpan InitialRetryInterval { get; private set; }

        /// <summary>
        /// Returns the maximum intervaL between action retry attempts. 
        /// </summary>
        public TimeSpan MaxRetryInterval { get; private set; }

        /// <inheritdoc/>
        public override IRetryPolicy Clone(Func<Exception, bool> transientDetector = null)
        {
            if (transientDetector == null)
            {
                // The class is invariant we can safely return ourself
                // when we're retaining the current transient detector.

                return this;
            }
            else
            {
                return new ExponentialRetryPolicy(transientDetector ?? this.transientDetector, MaxAttempts, InitialRetryInterval, MaxRetryInterval, Timeout, SourceModule);
            }
        }

        /// <inheritdoc/>
        public override async Task InvokeAsync(Func<Task> action)
        {
            var attempts = 0;
            var sysDeadline = base.SysDeadline();
            var interval = InitialRetryInterval;

            while (true)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception e)
                {
                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    await Task.Delay(adjustedDelay);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action)
        {
            var attempts = 0;
            var sysDeadline = base.SysDeadline();
            var interval = InitialRetryInterval;

            while (true)
            {
                try
                {
                    return await action();
                }
                catch (Exception e)
                {
                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || !transientDetector(e) || adjustedDelay <= TimeSpan.Zero)
                    {
                        throw;
                    }

                    LogTransient(e);
                    await Task.Delay(adjustedDelay);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void Invoke(Action action)
        {
            var attempts = 0;
            var sysDeadline = base.SysDeadline();
            var interval = InitialRetryInterval;

            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception e)
                {
                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    Thread.Sleep(adjustedDelay);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override TResult Invoke<TResult>(Func<TResult> action)
        {
            var attempts = 0;
            var sysDeadline = base.SysDeadline();
            var interval = InitialRetryInterval;

            while (true)
            {
                try
                {
                    return action();
                }
                catch (Exception e)
                {
                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || !transientDetector(e) || adjustedDelay <= TimeSpan.Zero)
                    {
                        throw;
                    }

                    LogTransient(e);
                    Thread.Sleep(adjustedDelay);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }
    }
}