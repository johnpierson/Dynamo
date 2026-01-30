using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Dynamo.Engine;
using Dynamo.Interfaces;
using Dynamo.Logging;
using Dynamo.Utilities;

namespace Dynamo.Documentation
{
    /// <summary>
    /// Validates that all nodes in the library have corresponding documentation files
    /// </summary>
    public class DocumentationValidator : LogSourceBase
    {
        private readonly LibraryServices libraryServices;
        private readonly IPathManager pathManager;
        private readonly DirectoryInfo dynamoCoreFallbackDocPath;
        private readonly DirectoryInfo hostDynamoFallbackDocPath;
        private const string FALLBACK_DOC_DIRECTORY_NAME = "fallback_docs";

        /// <summary>
        /// Initializes a new instance of the DocumentationValidator
        /// </summary>
        /// <param name="libraryServices">The library services instance containing all nodes</param>
        /// <param name="pathManager">The path manager for resolving documentation paths</param>
        public DocumentationValidator(LibraryServices libraryServices, IPathManager pathManager)
        {
            this.libraryServices = libraryServices ?? throw new ArgumentNullException(nameof(libraryServices));
            this.pathManager = pathManager ?? throw new ArgumentNullException(nameof(pathManager));

            // Initialize fallback documentation paths (same logic as PackageDocumentationManager)
            if (!string.IsNullOrEmpty(pathManager.DynamoCoreDirectory))
            {
                var coreDir = new DirectoryInfo(Path.Combine(pathManager.DynamoCoreDirectory,
                    Thread.CurrentThread.CurrentCulture.ToString(), FALLBACK_DOC_DIRECTORY_NAME));
                if (!coreDir.Exists)
                {
                    coreDir = new DirectoryInfo(Path.Combine(pathManager.DynamoCoreDirectory,
                        "en-US", FALLBACK_DOC_DIRECTORY_NAME));
                }
                dynamoCoreFallbackDocPath = coreDir.Exists ? coreDir : null;
            }

            if (!string.IsNullOrEmpty(pathManager.HostApplicationDirectory))
            {
                var hostDir = new DirectoryInfo(Path.Combine(pathManager.HostApplicationDirectory,
                    Thread.CurrentThread.CurrentCulture.ToString(), FALLBACK_DOC_DIRECTORY_NAME));
                if (!hostDir.Exists)
                {
                    hostDir = new DirectoryInfo(Path.Combine(pathManager.HostApplicationDirectory,
                        "en-US", FALLBACK_DOC_DIRECTORY_NAME));
                }
                hostDynamoFallbackDocPath = hostDir.Exists ? hostDir : null;
            }
        }

        /// <summary>
        /// Validates all nodes in the library and generates a report
        /// </summary>
        /// <returns>A ValidationReport containing results for all nodes</returns>
        public ValidationReport ValidateAllNodes()
        {
            var report = new ValidationReport();
            Log("Starting documentation validation...");

            // Get all function groups from both builtin and imported libraries
            var allFunctionGroups = libraryServices.BuiltinFunctionGroups
                .Concat(libraryServices.ImportedFunctionGroups);

            // Get all unique function descriptors, filtering out hidden nodes
            var allFunctions = allFunctionGroups
                .SelectMany(g => g.Functions)
                .Where(f => f.IsVisibleInLibrary)
                .GroupBy(f => f.QualifiedName)
                .Select(g => g.First()) // Take only one if there are overloads
                .OrderBy(f => f.QualifiedName)
                .ToList();

            Log($"Validating {allFunctions.Count} visible nodes...");

            foreach (var function in allFunctions)
            {
                var result = ValidateNode(function);
                report.NodeResults.Add(result);
            }

            // Calculate summary statistics
            report.Summary.TotalNodes = report.NodeResults.Count;
            report.Summary.NodesWithMarkdown = report.NodeResults.Count(n => n.HasMarkdown);
            report.Summary.NodesMissingMarkdown = report.NodeResults.Count(n => !n.HasMarkdown);

            Log($"Validation complete. {report.Summary.NodesWithMarkdown}/{report.Summary.TotalNodes} " +
                $"nodes have documentation ({report.Summary.DocumentationCompleteness:F2}%)");

            return report;
        }

        /// <summary>
        /// Validates a single node's documentation
        /// </summary>
        /// <param name="function">The function descriptor to validate</param>
        /// <returns>Validation result for the node</returns>
        private NodeValidationResult ValidateNode(FunctionDescriptor function)
        {
            var result = new NodeValidationResult
            {
                QualifiedName = function.QualifiedName,
                DisplayName = function.DisplayName,
                Category = function.Category
            };

            // Try to find markdown documentation
            var markdownPath = FindMarkdownFile(function.QualifiedName);

            if (!string.IsNullOrEmpty(markdownPath))
            {
                result.HasMarkdown = true;
                result.MarkdownPath = markdownPath;
            }
            else
            {
                result.HasMarkdown = false;
                result.Issues.Add("No markdown documentation file found");
            }

            return result;
        }

        /// <summary>
        /// Attempts to find a markdown documentation file for the given node namespace
        /// </summary>
        /// <param name="nodeNamespace">The qualified name of the node</param>
        /// <returns>Full path to the markdown file if found, otherwise empty string</returns>
        private string FindMarkdownFile(string nodeNamespace)
        {
            // Try hash-based filename first (most common)
            var shortName = Hash.GetHashFilenameFromString(nodeNamespace);

            FileInfo matchingDoc = null;

            // Check host fallback directory first
            if (hostDynamoFallbackDocPath != null)
            {
                matchingDoc = hostDynamoFallbackDocPath.GetFiles($"{shortName}.md").FirstOrDefault() ??
                              hostDynamoFallbackDocPath.GetFiles($"{nodeNamespace}.md").FirstOrDefault();
                if (matchingDoc != null)
                {
                    return matchingDoc.FullName;
                }
            }

            // Check Dynamo Core fallback directory
            if (dynamoCoreFallbackDocPath != null)
            {
                matchingDoc = dynamoCoreFallbackDocPath.GetFiles($"{shortName}.md").FirstOrDefault() ??
                              dynamoCoreFallbackDocPath.GetFiles($"{nodeNamespace}.md").FirstOrDefault();
            }

            return matchingDoc?.FullName ?? string.Empty;
        }
    }
}
