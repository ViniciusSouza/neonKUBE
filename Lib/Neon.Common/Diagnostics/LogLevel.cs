﻿//-----------------------------------------------------------------------------
// FILE:	    LogLevel.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2017 by NeonForge, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Neon.Diagnostics
{
    /// <summary>
    /// Enumerates the possible log levels.  Note that the relative
    /// ordinal values of  these definitions are used when deciding
    /// to log an event when a specific <see cref="LogLevel"/> is 
    /// set.  Only events with log levels less than or equal to the
    /// current level will be logged.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Logging is disabled.
        /// </summary>
        None = 0,

        /// <summary>
        /// A critical or fatal error has been detected.
        /// </summary>
        Critical = 100,

        /// <summary>
        /// A security related error has occurred.
        /// </summary>
        SError = 200,

        /// <summary>
        /// An error has been detected. 
        /// </summary>
        Error = 300,

        /// <summary>
        /// An unusual condition has been detected that may ultimately lead to an error.
        /// </summary>
        Warn = 400,

        /// <summary>
        /// Describes a normal operation or condition.
        /// </summary>
        Info = 500,

        /// <summary>
        /// Describes a non-error security operation or condition, such as a 
        /// a login or authentication.
        /// </summary>
        SInfo = 600,

        /// <summary>
        /// Describes detailed debug or diagnostic information.
        /// </summary>
        Debug = 700
    }
}
