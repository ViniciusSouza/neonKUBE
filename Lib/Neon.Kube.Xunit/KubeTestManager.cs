﻿//-----------------------------------------------------------------------------
// FILE:	    KubeTestManager.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.IO;
using Neon.Kube;
using Neon.Kube.Xunit;
using Neon.Xunit;

using Xunit;

namespace Neon.Kube.Xunit
{
    /// <summary>
    /// Creates a temporary folder and puts <see cref="KubeHelper"/> into test mode
    /// to support <b>neon-cli</b> unit testing.  <see cref="Dispose"/> reverts the 
    /// test mode and deletes the temporary folder.
    /// </summary>
    public sealed class KubeTestManager : IDisposable
    {
        //---------------------------------------------------------------------
        // Static members

        private static readonly object syncLock = new object();

        /// <summary>
        /// Returns the current test manager.
        /// </summary>
        public static KubeTestManager Current { get; private set; }

        //---------------------------------------------------------------------
        // Instance members

        private TempFolder tempFolder;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="mode">Optionally specifies the test mode.  This defaults to <see cref="KubeAutomationMode.EnabledWithSharedCache"/>.</param>
        /// <exception cref="InvalidOperationException">Thrown if another test manager instance is active.</exception>
        public KubeTestManager(KubeAutomationMode mode = KubeAutomationMode.EnabledWithSharedCache)
        {
            lock (syncLock)
            {
                if (Current != null)
                {
                    throw new InvalidOperationException("Another test manager is already active.");
                }

                try
                {
                    tempFolder = new TempFolder();
                    Current    = this;

                    KubeHelper.SetAutomationMode(tempFolder.Path, mode);
                }
                catch
                {
                    KubeHelper.ResetAutomationMode();
                    Current = null;
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            KubeHelper.ResetAutomationMode();

            if (tempFolder != null)
            {
                tempFolder.Dispose();

                tempFolder = null;
                Current    = null;
            }
        }

        /// <summary>
        /// Returns the path to the temporary test folder.
        /// </summary>
        public string TestFolder => tempFolder?.Path;
    }
}
