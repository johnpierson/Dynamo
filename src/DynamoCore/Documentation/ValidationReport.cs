using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynamo.Documentation
{
    /// <summary>
    /// Represents the validation result for a single node's documentation
    /// </summary>
    public class NodeValidationResult
    {
        /// <summary>
        /// Qualified name of the node (e.g., "List.Count")
        /// </summary>
        public string QualifiedName { get; set; }

        /// <summary>
        /// Display name of the node
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Category path of the node
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Whether a .md documentation file was found for this node
        /// </summary>
        public bool HasMarkdown { get; set; }

        /// <summary>
        /// Path to the markdown file if found
        /// </summary>
        public string MarkdownPath { get; set; }

        /// <summary>
        /// List of validation issues found
        /// </summary>
        public List<string> Issues { get; set; }

        public NodeValidationResult()
        {
            Issues = new List<string>();
        }
    }

    /// <summary>
    /// Summary statistics for documentation validation
    /// </summary>
    public class ValidationSummary
    {
        /// <summary>
        /// Total number of nodes checked
        /// </summary>
        public int TotalNodes { get; set; }

        /// <summary>
        /// Number of nodes missing markdown documentation
        /// </summary>
        public int NodesMissingMarkdown { get; set; }

        /// <summary>
        /// Number of nodes with documentation
        /// </summary>
        public int NodesWithMarkdown { get; set; }

        /// <summary>
        /// Percentage of nodes with documentation (0-100)
        /// </summary>
        public double DocumentationCompleteness
        {
            get
            {
                if (TotalNodes == 0) return 0;
                return (double)NodesWithMarkdown / TotalNodes * 100;
            }
        }
    }

    /// <summary>
    /// Complete validation report for all node documentation
    /// </summary>
    public class ValidationReport
    {
        /// <summary>
        /// Timestamp when validation was performed
        /// </summary>
        public DateTime ValidationTime { get; set; }

        /// <summary>
        /// Summary statistics
        /// </summary>
        public ValidationSummary Summary { get; set; }

        /// <summary>
        /// Validation results for each node
        /// </summary>
        public List<NodeValidationResult> NodeResults { get; set; }

        public ValidationReport()
        {
            ValidationTime = DateTime.Now;
            Summary = new ValidationSummary();
            NodeResults = new List<NodeValidationResult>();
        }

        /// <summary>
        /// Gets nodes that are missing markdown documentation
        /// </summary>
        public IEnumerable<NodeValidationResult> GetNodesMissingMarkdown()
        {
            return NodeResults.Where(n => !n.HasMarkdown);
        }

        /// <summary>
        /// Gets nodes that have documentation
        /// </summary>
        public IEnumerable<NodeValidationResult> GetNodesWithMarkdown()
        {
            return NodeResults.Where(n => n.HasMarkdown);
        }

        /// <summary>
        /// Returns a formatted summary string
        /// </summary>
        public string GetSummaryString()
        {
            return $"Documentation Validation Summary:\n" +
                   $"  Total Nodes: {Summary.TotalNodes}\n" +
                   $"  Nodes with Markdown: {Summary.NodesWithMarkdown}\n" +
                   $"  Nodes missing Markdown: {Summary.NodesMissingMarkdown}\n" +
                   $"  Documentation Completeness: {Summary.DocumentationCompleteness:F2}%";
        }
    }
}
