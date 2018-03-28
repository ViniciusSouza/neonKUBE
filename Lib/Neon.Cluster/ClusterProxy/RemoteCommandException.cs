﻿//-----------------------------------------------------------------------------
// FILE:	    RemoteCommandException.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Consul;

using Neon.Common;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Time;

namespace Neon.Cluster
{
    /// <summary>
    /// Indicates that a remote command execution failed.
    /// </summary>
    public class RemoteCommandException : Exception
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The optional inner exception.</param>
        public RemoteCommandException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }
}
