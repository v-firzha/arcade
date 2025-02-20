// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.SignCheck.Logging;

namespace Microsoft.SignCheck.Verification
{
    public abstract class ArchiveVerifier : FileVerifier
    {
        protected ArchiveVerifier(Log log, Exclusions exclusions, SignatureVerificationOptions options, string fileExtension) : base(log, exclusions, options, fileExtension)
        {

        }

        /// <summary>
        /// Read the entries from the archive.
        /// </summary>
        /// <param name="archivePath">Path to the archive</param>
        protected abstract IEnumerable<ArchiveEntry> ReadArchiveEntries(string archivePath);

        /// <summary>
        /// Verify the contents of a package archive and add the results to the container result.
        /// </summary>
        /// <param name="svr">The container result</param>
        protected void VerifyContent(SignatureVerificationResult svr)
        {
            if (VerifyRecursive)
            {
                string tempPath = svr.TempPath;
                CreateDirectory(tempPath);
                Log.WriteMessage(LogVerbosity.Diagnostic, SignCheckResources.DiagExtractingFileContents, tempPath);
                Dictionary<string, string> archiveMap = new Dictionary<string, string>();

                foreach (ArchiveEntry archiveEntry in ReadArchiveEntries(svr.FullPath))
                {
                    string aliasFullName = GenerateArchiveEntryAlias(archiveEntry, tempPath);
                    if (File.Exists(aliasFullName))
                    {
                        Log.WriteMessage(LogVerbosity.Normal, SignCheckResources.FileAlreadyExists, aliasFullName);
                    }
                    else
                    {
                        CreateDirectory(Path.GetDirectoryName(aliasFullName));
                        WriteArchiveEntry(archiveEntry, aliasFullName);
                        archiveMap[archiveEntry.RelativePath] = aliasFullName;
                    }
                }

                // We can only verify once everything is extracted. This is mainly because MSIs can have mutliple external CAB files
                // and we need to ensure they are extracted before we verify the MSIs.
                foreach (string fullName in archiveMap.Keys)
                {
                    SignatureVerificationResult result = VerifyFile(archiveMap[fullName], svr.Filename,
                        Path.Combine(svr.VirtualPath, fullName), fullName);

                    // Tag the full path into the result detail
                    result.AddDetail(DetailKeys.File, SignCheckResources.DetailFullName, fullName);
                    svr.NestedResults.Add(result);
                }
                DeleteDirectory(tempPath);
            }
        }

        /// <summary>
        /// Writes the archive entry to the specified path.
        /// </summary>
        /// <param name="archiveEntry"></param>
        /// <param name="targetPath"></param>
        protected void WriteArchiveEntry(ArchiveEntry archiveEntry, string targetPath)
        {
            using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
            {
                archiveEntry.ContentStream.CopyTo(fileStream);
            }
        }

        /// <summary>
        /// Generates an alias for the actual file that has the same extension.
        /// We do this to avoid path too long errors so that containers can be flattened.
        /// </summary>
        /// <param name="archiveEntry">The archive entry to generate the alias for.</param>
        /// <param name="tempPath">The temporary path for the archive entry.</param>
        private string GenerateArchiveEntryAlias(ArchiveEntry archiveEntry, string tempPath)
        {
            // Generate an alias for the actual file that has the same extension. We do this to avoid path too long errors so that
            // containers can be flattened.
            string directoryName = Path.GetDirectoryName(archiveEntry.RelativePath);
            string hashedPath = String.IsNullOrEmpty(directoryName) ? Utils.GetHash(@".\", HashAlgorithmName.SHA256.Name) :
                Utils.GetHash(directoryName, HashAlgorithmName.SHA256.Name);
            string extension = Path.GetExtension(archiveEntry.RelativePath);

            // CAB files cannot be aliased since they're referred to from the Media table inside the MSI
            string aliasFileName = String.Equals(extension.ToLowerInvariant(), ".cab") ? Path.GetFileName(archiveEntry.RelativePath) :
                Utils.GetHash(archiveEntry.RelativePath, HashAlgorithmName.SHA256.Name) + Path.GetExtension(archiveEntry.RelativePath); // lgtm [cs/zipslip] Archive from trusted source

            return Path.Combine(tempPath, hashedPath, aliasFileName);
        }

        /// <summary>
        /// Represents an entry in an archive.
        /// </summary>
        protected class ArchiveEntry
        {
            public string RelativePath { get; set; }
            public Stream ContentStream { get; set; }
            public long ContentSize { get; set; }
        }
    }
}
