﻿//-----------------------------------------------------------------------------
// FILE:	    ActivityExecuteRequest.cs
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
using System.Collections.Generic;
using System.ComponentModel;

using Neon.Common;
using Neon.Temporal;

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// <b>client --> proxy:</b> Starts a (non-local) workflow activity.
    /// </summary>
    [InternalProxyMessage(InternalMessageTypes.ActivityExecuteRequest)]
    internal class ActivityExecuteRequest : ActivityRequest
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ActivityExecuteRequest()
        {
            Type = InternalMessageTypes.ActivityExecuteRequest;
        }

        /// <inheritdoc/>
        public override InternalMessageTypes ReplyType => InternalMessageTypes.ActivityExecuteReply;

        /// <summary>
        /// Specifies the activity to execute
        /// </summary>
        public string Activity
        {
            get => GetStringProperty(PropertyNames.Activity);
            set => SetStringProperty(PropertyNames.Activity, value);
        }

        /// <summary>
        /// Optionally specifies the arguments to be passed to the activity encoded
        /// as a byte array.
        /// </summary>
        public byte[] Args
        {
            get => GetBytesProperty(PropertyNames.Args);
            set => SetBytesProperty(PropertyNames.Args, value);
        }

        /// <summary>
        /// The activity start options.
        /// </summary>
        public ActivityOptions Options
        {
            get => GetJsonProperty<ActivityOptions>(PropertyNames.Options);
            set => SetJsonProperty<ActivityOptions>(PropertyNames.Options, value);
        }

        /// <inheritdoc/>
        internal override ProxyMessage Clone()
        {
            var clone = new ActivityExecuteRequest();

            CopyTo(clone);

            return clone;
        }

        /// <inheritdoc/>
        protected override void CopyTo(ProxyMessage target)
        {
            base.CopyTo(target);

            var typedTarget = (ActivityExecuteRequest)target;

            typedTarget.Activity = this.Activity;
            typedTarget.Args     = this.Args;
            typedTarget.Options  = this.Options;
        }
    }
}
