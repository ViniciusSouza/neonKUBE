﻿//-----------------------------------------------------------------------------
// FILE:	    Test_Packages.cs
// CONTRIBUTOR: Marcus Bowyer
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

using Neon.Common;
using Neon.Deployment;
using Neon.IO;
using Neon.Xunit;

namespace TestDeployment
{
    // IMPORTANT NOTE!:
    // ----------------
    // These unit tests require that [neon-assistant] be running.

    [Trait(TestTrait.Category, TestArea.NeonDeployment)]
    [Trait(TestTrait.Category, TestTrait.Investigate)]      // https://github.com/nforgeio/neonCLOUD/issues/149
    [Collection(TestCollection.NonParallel)]
    [CollectionDefinition(TestCollection.NonParallel, DisableParallelization = true)]
    public partial class Test_Packages
    {
        [Fact(Skip = "Must be run manually")]
        public async void ListPackages()
        {
            // Verify that we can get the list of packages.

            var client   = new GitHubPackageApi();
            var packages = await client.ListAsync("neon-test");

            Assert.NotEmpty(packages);

            packages = await client.ListAsync("neon-test", "test");

            Assert.NotEmpty(packages);

            packages = await client.ListAsync("neon-test", "test*");

            Assert.NotEmpty(packages);
        }

        [Fact(Skip = "Must be run manually")]
        public async void MakePublic()
        {
            // Verify that we can make a package public.

            var client = new GitHubPackageApi();

            await client.SetVisibilityAsync("neon-test", "test", visibility: GitHubPackageVisibility.Public);

            var packages = await client.ListAsync("neon-test", "test", visibility: GitHubPackageVisibility.Public);

            Assert.Contains(packages, p => p.Name == "test");
        }

        [Fact(Skip = "Must be run manually")]
        public async void MakePrivate()
        {
            // Verify that we can make a package private.

            var client = new GitHubPackageApi();

            await client.SetVisibilityAsync("neon-test", "test", visibility: GitHubPackageVisibility.Private);

            var packages = await client.ListAsync("neon-test", "test", visibility: GitHubPackageVisibility.Private);

            Assert.Contains(packages, p => p.Name == "test");
        }

        [Fact(Skip = "$todo(marcusbooyah")]
        [Trait(TestTrait.Category, TestTrait.Incomplete)]
        public async void Delete()
        {
            var client = new GitHubPackageApi();

            //await client.DeleteAsync("neonrelease-dev", "test");
            await Task.CompletedTask;
        }
    }
}
