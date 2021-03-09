﻿//-----------------------------------------------------------------------------
// FILE:	    Test_PasswordCommands.cs
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
using Neon.Kube;
using Neon.Kube.Xunit;
using Neon.IO;
using Neon.Xunit;

using Xunit;
using NeonCli;

// $todo(jefflill): 
//
// We're not currently testing prompting actions by these commands.

namespace Test.NeonCli
{
    /// <summary>
    /// Tests <b>neon passwords</b> commands.
    /// </summary>
    [Collection(TestCollection.NonParallel)]
    [CollectionDefinition(TestCollection.NonParallel, DisableParallelization = true)]
    public class Test_PasswordCommands
    {
        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public async Task Password()
        {
            ExecuteResponse result;

            using (new KubeTestManager())
            {
                using (var runner = new ProgramRunner())
                {
                    // Verify that [neon password] returns help/usage text:

                    result = await runner.ExecuteAsync(Program.Main, "password");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Manages neonKUBE passwords.", result.OutputText);

                    result = await runner.ExecuteAsync(Program.Main, "help", "password");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Manages neonKUBE passwords.", result.OutputText);

                    // Verify that the "--help" option does the same thing.

                    result = await runner.ExecuteAsync(Program.Main, "password", "--help");

                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Manages neonKUBE passwords.", result.OutputText);

                    // Verify that an invalid command fails.

                    result = await runner.ExecuteAsync(Program.Main, "password", "bad");

                    Assert.NotEqual(0, result.ExitCode);
                    Assert.Contains("Unexpected [bad] command.", result.ErrorText);
                }
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public async Task PasswordBasics()
        {
            ExecuteResponse result;

            // Verify basic password operations: get, set, list|ls, and remove|rm:

            using (var manager = new KubeTestManager())
            {
                using (var runner = new ProgramRunner())
                {
                    // We should start out with no passwords:

                    result = await runner.ExecuteAsync(Program.Main, "password", "list");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Empty(result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "ls");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Empty(result.OutputText.Trim());

                    // Add a few passwords via files and verify:

                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-1"), "one");
                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-2"), "two");
                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-3"), "three");

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-1", Path.Combine(manager.TestFolder, "pwd-1"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-2", Path.Combine(manager.TestFolder, "pwd-2"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-3", Path.Combine(manager.TestFolder, "pwd-3"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-1");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("one", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-2");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("two", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-3");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("three", result.OutputText.Trim());

                    // Verify that we can list the passwords:

                    result = await runner.ExecuteAsync(Program.Main, "password", "ls");
                    Assert.Equal(0, result.ExitCode);
                    TestHelper.AssertEqualLines(
@"pwd-1
pwd-2
pwd-3
",
                        result.OutputText);

                    // Verify that we can remove a specific password.

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "pwd-2");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "ls");
                    Assert.Equal(0, result.ExitCode);
                    TestHelper.AssertEqualLines(
    @"pwd-1
pwd-3
",
                        result.OutputText);

                    // Verify that we can remove all passwords:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "ls");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Empty(result.OutputText);
                }
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public async Task PasswordSet()
        {
            ExecuteResponse result;

            using (var manager = new KubeTestManager())
            {
                using (var runner = new ProgramRunner())
                {
                    // Verify that [--help] works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "set", "--help");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Creates or modifies a named password.", result.OutputText);

                    // Add a few passwords via files and verify:

                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-1"), "one");
                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-2"), "two");
                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-3"), "three");

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-1", Path.Combine(manager.TestFolder, "pwd-1"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-2", Path.Combine(manager.TestFolder, "pwd-2"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-3", Path.Combine(manager.TestFolder, "pwd-3"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-1");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("one", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-2");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("two", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-3");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("three", result.OutputText.Trim());

                    // Verify that we can set a password from STDIN:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "one", "password", "set", "pwd-1", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "two", "password", "set", "pwd-2", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "three", "password", "set", "pwd-3", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-1");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("one", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-2");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("two", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-3");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("three", result.OutputText.Trim());

                    // Verify that we can update a password.

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "one", "password", "set", "pwd-1", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-1");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("one", result.OutputText.Trim());

                    result = await runner.ExecuteWithInputAsync(Program.Main, "1", "password", "set", "pwd-1", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-1");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("1", result.OutputText.Trim());

                    // Verify that password names with all possible character classes works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "password", "password", "set", "a.1_2-3", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "a.1_2-3");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("password", result.OutputText.Trim());

                    // Verify that a 20 character password is generated when no PATH argument is passed:

                    result = await runner.ExecuteWithInputAsync(Program.Main, "password", "password", "set", "abc");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "abc");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal(20, result.OutputText.Trim().Length);

                    // Verify that we see errors for missing arguments:

                    result = await runner.ExecuteWithInputAsync(Program.Main, "password", "password", "set");
                    Assert.NotEqual(0, result.ExitCode);
                    Assert.Contains("NAME argument is required.", result.ErrorText);

                    // Verify that password name error checking works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "", "password", "set", "pwd@1", "-");
                    Assert.NotEqual(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "", $"password", "set", $"{new string('a', 101)}", "-");
                    Assert.NotEqual(0, result.ExitCode);

                    // Verify that password length error checking works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "", "password", "set", "pwd-1", "-");
                    Assert.NotEqual(0, result.ExitCode);
                }
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public async Task PasswordGenerate()
        {
            ExecuteResponse result;

            using (var manager = new KubeTestManager())
            {
                using (var runner = new ProgramRunner())
                {
                    //// Verify that [--help] works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "--help");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Generates a cryptographically secure password.", result.OutputText);

                    // Verify that we can generate a password with the default length.

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal(20, result.OutputText.Trim().Length);

                    // Verify that we can generate a password with a specific length.

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "30");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal(30, result.OutputText.Trim().Length);

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "8");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal(8, result.OutputText.Trim().Length);

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "100");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal(100, result.OutputText.Trim().Length);

                    // Verify that invalid password lengths are detected.

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "BAD");
                    Assert.NotEqual(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "0");
                    Assert.NotEqual(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "7");
                    Assert.NotEqual(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "generate", "101");
                    Assert.NotEqual(0, result.ExitCode);

                    // Verify that we get different passwords when we run this
                    // multiple times.

                    var previousPasswords = new HashSet<string>();

                    for (int i = 0; i < 50; i++)
                    {
                        result = await runner.ExecuteAsync(Program.Main, "password", "generate", "100");
                        Assert.Equal(0, result.ExitCode);

                        var password = result.OutputText.Trim();

                        Assert.DoesNotContain(previousPasswords, p => p == password);
                        previousPasswords.Add(password);
                    }
                }
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public async Task PasswordRemove()
        {
            ExecuteResponse result;

            using (var manager = new KubeTestManager())
            {
                using (var runner = new ProgramRunner())
                {
                    // Verify that [--help] works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "remove", "--help");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Removes a specific named password or all passwords.", result.OutputText);

                    // Add a few passwords:

                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-1"), "one");
                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-2"), "two");
                    File.WriteAllText(Path.Combine(manager.TestFolder, "pwd-3"), "three");

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-1", Path.Combine(manager.TestFolder, "pwd-1"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-2", Path.Combine(manager.TestFolder, "pwd-2"));
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, $"password", "set", "pwd-3", Path.Combine(manager.TestFolder, "pwd-3"));
                    Assert.Equal(0, result.ExitCode);

                    // Verify that we can list the passwords:

                    result = await runner.ExecuteAsync(Program.Main, "password", "ls");
                    Assert.Equal(0, result.ExitCode);
                    TestHelper.AssertEqualLines(
    @"pwd-1
pwd-2
pwd-3
",
                        result.OutputText);

                    // Verify that we can remove a specific password.

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "pwd-2");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "ls");
                    Assert.Equal(0, result.ExitCode);
                    TestHelper.AssertEqualLines(
    @"pwd-1
pwd-3
",
                        result.OutputText);

                    // Verify that we can remove all passwords:

                    result = await runner.ExecuteAsync(Program.Main, "password", "remove", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "list");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Empty(result.OutputText);

                    // Verify that we see errors for missing arguments:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm");
                    Assert.NotEqual(0, result.ExitCode);
                    Assert.Contains("NAME argument is required.", result.ErrorText);

                    // Verify what we see an error when trying to remove a password
                    // that doesn't exist:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "BAD");
                    Assert.NotEqual(0, result.ExitCode);
                    Assert.Contains("does not exist", result.ErrorText);
                }
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public async Task PasswordList()
        {
            ExecuteResponse result;

            using (var manager = new KubeTestManager())
            {
                using (var runner = new ProgramRunner())
                {
                    // Verify that [--help] works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "list", "--help");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Lists passwords.", result.OutputText);

                    // Add a few passwords:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "one", "password", "set", "pwd-1", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "two", "password", "set", "pwd-2", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "three", "password", "set", "pwd-3", "-");
                    Assert.Equal(0, result.ExitCode);

                    // Verify that we can list via: list

                    result = await runner.ExecuteAsync(Program.Main, "password", "list");
                    Assert.Equal(0, result.ExitCode);
                    TestHelper.AssertEqualLines(
@"pwd-1
pwd-2
pwd-3
",
                        result.OutputText);

                    // Verify that we can list via: ls

                    result = await runner.ExecuteAsync(Program.Main, "password", "ls");
                    Assert.Equal(0, result.ExitCode);
                    TestHelper.AssertEqualLines(
@"pwd-1
pwd-2
pwd-3
",
                        result.OutputText);
                }
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public async Task PasswordImportExport()
        {
            const string zipPassword = "zip-password";

            ExecuteResponse result;

            using (var manager = new KubeTestManager())
            {
                using (var runner = new ProgramRunner())
                {
                    // Verify that [import --help] works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "import", "--help");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Imports passwords from an encrypted ZIP file.", result.OutputText);

                    // Verify that [import] checks the PATH argument.

                    result = await runner.ExecuteAsync(Program.Main, "password", "import");
                    Assert.NotEqual(0, result.ExitCode);
                    Assert.Contains("PATH argument is required.", result.ErrorText);

                    // Verify that [export --help] works:

                    result = await runner.ExecuteAsync(Program.Main, "password", "export", "--help");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Contains("Exports selected passwords to an encrypted ZIP file.", result.OutputText);

                    // Verify that [export] checks the PATH argument.

                    result = await runner.ExecuteAsync(Program.Main, "password", "export");
                    Assert.NotEqual(0, result.ExitCode);
                    Assert.Contains("PATH argument is required.", result.ErrorText);

                    // Verify that [export] checks the NAME argument.

                    result = await runner.ExecuteAsync(Program.Main, "password", "export", "test.zip");
                    Assert.NotEqual(0, result.ExitCode);
                    Assert.Contains("At least one NAME argument is required.", result.ErrorText);

                    // Add a few passwords:

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "one", "password", "set", "pwd-1", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "two", "password", "set", "pwd-2", "-");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, "three", "password", "set", "pwd-3", "-");
                    Assert.Equal(0, result.ExitCode);

                    // Export all passwords to a ZIP file:

                    var zipPath = Path.Combine(manager.TestFolder, "passwords.zip");

                    result = await runner.ExecuteWithInputAsync(Program.Main, zipPassword, "password", "export", "--stdin", zipPath, "*");
                    Assert.Equal(0, result.ExitCode);
                    Assert.True(File.Exists(zipPath));

                    // Remove all passwords, import the passwords using a zip password file, and verify.

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, zipPassword, "password", "import", "--stdin", zipPath);
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-1");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("one", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-2");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("two", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-3");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("three", result.OutputText.Trim());

                    // Export two of the three passwords to a ZIP file:

                    result = await runner.ExecuteWithInputAsync(Program.Main, zipPassword, "password", "export", "--stdin", zipPath, "pwd-1", "pwd-2");
                    Assert.Equal(0, result.ExitCode);
                    Assert.True(File.Exists(zipPath));

                    // Remove all passwords, import the passwords using a zip password file, and verify.

                    result = await runner.ExecuteAsync(Program.Main, "password", "rm", "--force", "*");
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteWithInputAsync(Program.Main, zipPassword, "password", "import", "--stdin", zipPath);
                    Assert.Equal(0, result.ExitCode);

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-1");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("one", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-2");
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal("two", result.OutputText.Trim());

                    result = await runner.ExecuteAsync(Program.Main, "password", "get", "pwd-3");
                    Assert.NotEqual(0, result.ExitCode);    // This one wasn't exported.
                }
            }
        }
    }
}