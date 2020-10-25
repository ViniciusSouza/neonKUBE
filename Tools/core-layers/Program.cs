﻿//-----------------------------------------------------------------------------
// FILE:	    Program.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE LLC.  All rights reserved.
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
using System.IO;

namespace CoreLayers
{
    /// <summary>
    /// This program segments the files generated by the compiler for a .NET Core
    /// application into two sub folders so that Docker images can be optimized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This program segments the files generated by the compiler for a .NET Core
    /// application into two sub folders called <b>__app</b> and <b>__dep</b>.
    /// The <b>__app</b> folder will include all application specific files and
    /// the <b>__dep</b> folder will include all of the assemblies and
    /// packages the application depends on.
    /// </para>
    /// <para>
    /// The idea here is that that tool will be used by Docker image generation
    /// scripts so that an image layer with just the dependencies can be created
    /// followed by the application layer.  This should tend to optimize images
    /// because dependencies will like change less frequently and it's also likely
    /// that multiple application images may share the same dependencies.
    /// </para>
    /// <para>
    /// Usage: core-layers ASSEMBLY PATH
    /// </para>
    /// <para>
    /// Where <b>ASSEMBLY</b> is the application assembly name with no file extension and
    /// <b>PATH</b> is the path to the folder where the compiler generated the application's
    /// binaries.
    /// </para>
    /// <para>
    /// The two folders will be created under <b>PATH</b>.  All <b>*.dll</b>, <b>*.pdb</b>,
    /// and <b>*.xml</b> files whose file name is not <b>ASSEMBLY</b> along with the <b>runtimes</b> 
    /// folder will be copied to <b>PATH/__deps</b>.  All other files will be copied to <b>PATH/__app</b>.
    /// </para>
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Tool version number.
        /// </summary>
        public const string Version = "1.0";

        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static void Main(string[] args)
        {
            var usage = 
$@"
Entity Code Generator v{Version}

usage: core-layers ASSEMBLY PATH";

            if (args.Length != 2)
            {
                Console.WriteLine(usage);
                Program.Exit(1);
            }

            var assembly = args[0];
            var path     = Path.GetFullPath(args[1]);
            var appPath  = Path.Combine(path, "__app");
            var depPath  = Path.Combine(path, "__dep");

            // Start out with empty [__app] and [__dep] folders.

            if (Directory.Exists(appPath))
            {
                Directory.Delete(appPath, recursive: true);
            }

            if (Directory.Exists(depPath))
            {
                Directory.Delete(depPath, recursive: true);
            }

            Directory.CreateDirectory(appPath);
            Directory.CreateDirectory(depPath);

            // Copy the files.

            var runtimesPath = "runtimes" + Path.DirectorySeparatorChar;

            foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(filePath);

                string relativePath;
                string copyPath;

                if (Path.GetExtension(fileName).Equals(".dll", StringComparison.InvariantCultureIgnoreCase) ||
                    Path.GetExtension(fileName).Equals(".pdb", StringComparison.InvariantCultureIgnoreCase) ||
                    Path.GetExtension(fileName).Equals(".xml", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (Path.GetDirectoryName(filePath).Equals(path) &&
                        !Path.GetFileNameWithoutExtension(fileName).Equals(assembly, StringComparison.InvariantCultureIgnoreCase))
                    {
                        // Looks like this is a simple dependency file.

                        File.Copy(filePath, Path.Combine(depPath, fileName));
                        continue;
                    }
                }

                relativePath = filePath.Substring(path.Length + 1);

                if (relativePath.StartsWith(runtimesPath, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Looks like a file in the runtimes folder.

                    copyPath = Path.Combine(depPath, relativePath);

                    Directory.CreateDirectory(Path.GetDirectoryName(copyPath));
                    File.Copy(filePath, copyPath);
                    continue;
                }

                // Look like this is an app file.  Note that app files MAY be nested
                // in folders below the binary path.

                copyPath = Path.Combine(appPath, relativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(copyPath));
                File.Copy(filePath, copyPath);
            }

            Program.Exit(0);
        }

        /// <summary>
        /// Exits the program returning the specified process exit code.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public static void Exit(int exitCode)
        {
            Environment.Exit(exitCode);
        }
    }
}
