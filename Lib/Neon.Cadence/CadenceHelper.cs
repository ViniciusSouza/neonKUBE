﻿//-----------------------------------------------------------------------------
// FILE:	    CadenceHelper.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Neon.Common;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Tasks;

using Neon.Cadence.Internal;

namespace Neon.Cadence
{
    /// <summary>
    /// Cadence helper methods and constants.
    /// </summary>
    internal static class CadenceHelper
    {
        /// <summary>
        /// Number of nanoseconds per second.
        /// </summary>
        public const long NanosecondsPerSecond = 1000000000L;

        /// <summary>
        /// Returns the maximum timespan supported by Cadence.
        /// </summary>
        public static TimeSpan MaxTimespan { get; private set; } = TimeSpan.FromTicks(long.MaxValue / 100);

        /// <summary>
        /// Returns the minimum timespan supported by Cadence.
        /// </summary>
        public static TimeSpan MinTimespan { get; private set; } = TimeSpan.FromTicks(long.MinValue / 100);

        /// <summary>
        /// Ensures that the timespan passed doesn't exceed the minimum or maximum
        /// supported by Cadence/GOLANG.
        /// </summary>
        /// <param name="timespan">The input.</param>
        /// <returns>The adjusted output.</returns>
        public static TimeSpan Normalize(TimeSpan timespan)
        {
            if (timespan > MaxTimespan)
            {
                return MaxTimespan;
            }
            else if (timespan < MinTimespan)
            {
                return MinTimespan;
            }
            else
            {
                return timespan;
            }
        }

        /// <summary>
        /// Converts a .NET <see cref="TimeSpan"/> into a Cadence/GOLANG duration
        /// (aka a <c>long</c> specifying the interval in nanoseconds.
        /// </summary>
        /// <param name="timespan">The input .NET timespan.</param>
        /// <returns>The duration in nanoseconds.</returns>
        public static long ToCadence(TimeSpan timespan)
        {
            timespan = Normalize(timespan);

            return timespan.Ticks * 100;
        }
    }
}
