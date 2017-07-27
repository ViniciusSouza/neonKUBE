﻿//-----------------------------------------------------------------------------
// FILE:	    Program.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2017 by NeonForge, LLC.  All rights reserved.

using System;
using System.IO;

namespace CoreLayers
{
    /// <summary>
    /// This program segments the files generated by the compiler for a NETCore
    /// application into two sub folders so that Docker images can be optimized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This program segments the files generated by the compiler for a NETCore
    /// application into two sub folders called <b>__app</b> and <b>__dep</b>.
    /// The <b>__app</b> folder will include all application specific files and
    /// the <b>__dep</b> folder will include all of the assemblies and
    /// packages the application depends on.
    /// </para>
    /// <para>
    /// The idea here is that that tool will be used by Docker image generation
    /// scripts so that an image layer with just the dependencies can be created
    /// followed by the application layer.  This should tend to optimize images
    /// because depdencies will like change less frequently and it's also likely
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
    /// The two folders will be created under <b>PATH</b>.  All <b>*.dll</b> and <b>*.pdb</b>
    /// files whose file name is not <b>ASSEMBLY</b> will be copied to <b>PATH/__deps</b> and
    /// all other files will be copied to <b>PATH/__app</b>.
    /// </para>
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: core-layers ASSEMBLY PATH");
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

            foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(filePath);

                if (Path.GetExtension(fileName).Equals(".dll", StringComparison.InvariantCultureIgnoreCase) ||
                    Path.GetExtension(fileName).Equals(".pdb", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (Path.GetDirectoryName(filePath).Equals(path) &&
                        !Path.GetFileNameWithoutExtension(fileName).Equals(assembly, StringComparison.InvariantCultureIgnoreCase))
                    {
                        // Looks like this is a dependency file.  Note that dependency
                        // files are NEVER nested in folders below the binary path.

                        File.Copy(filePath, Path.Combine(depPath, fileName));
                        continue;
                    }
                }

                // Look like this is an app file.  Note that app files MAY be nested
                // in folders below the binary path.

                var relativePath = filePath.Substring(path.Length + 1);
                var copyPath     = Path.Combine(appPath, relativePath);

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
