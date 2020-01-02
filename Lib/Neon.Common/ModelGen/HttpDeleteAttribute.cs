﻿//-----------------------------------------------------------------------------
// FILE:	    HttpDeleteAttribute.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2020 by neonFORGE, LLC.  All rights reserved.
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
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Text;

namespace Neon.ModelGen
{
    /// <summary>
    /// Used to identify a service endpoint that is triggered via the <b>DELETE</b> method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HttpDeleteAttribute : HttpAttribute
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="template">The optional routing template.</param>
        public HttpDeleteAttribute(string template = null)
        {
            this.Template   = template;
            this.HttpMethod = "DELETE";
        }
    }
}