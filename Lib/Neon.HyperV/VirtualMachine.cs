﻿//-----------------------------------------------------------------------------
// FILE:	    VirtualMachine.cs
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
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;

namespace Neon.HyperV
{
    /// <summary>
    /// Describes the state of a Hyper-V virtual machine.
    /// </summary>
    public class VirtualMachine
    {
        /// <summary>
        /// The machine name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The current machine state.
        /// </summary>
        public VirtualMachineState State { get; set; }

        /// <summary>
        /// Identifies the virtual switch to which this virtual machine is attached (or null).
        /// </summary>
        public string SwitchName { get; set; }

        /// <summary>
        /// Identifies the network interface or switch to which the address is assigned (or null).
        /// </summary>
        public string InterfaceName { get; set; }
    }
}
