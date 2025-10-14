using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiStudio4.Tools.Services.SmartFileEditor
{
    /// <summary>
    /// Smart file editor with intelligent error feedback
    /// </summary>
    public class SmartFileEditorService : ISmartFileEditor
    {
        private readonly ILogger<SmartFileEditorService> _logger;
        private const double PARTIAL_MATCH_THRESHOLD = 0.8;

        public SmartFileEditorService(ILogger<SmartFileEditorService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Applies a series of edits to a file
        /// </summary>
        public async Task<EditResult> ApplyEditsAsync(string filePath, List<FileEdit> edits)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new EditResult
                    {
                        Success = false,
                        ErrorMessage = $"File not found: {filePath}"
                    };
                }

                var content = await File.ReadAllTextAsync(filePath);
                
                foreach (var edit in edits)
                {
                    if(string.IsNullOrEmpty(edit.OldText))
                    {
                        return new EditResult
                        {
                            Success = false,
                            ErrorMessage = $"old_content must be set."
                        };
                    }

                    var result = ApplyEditToContent(content, edit);
                    
                    if (!result.Success)
                    {
                        _logger.LogWarning($"Edit failed: {result.ErrorMessage}");
                        return result;
                    }
                    
                    content = result.ModifiedContent;
                }

                await File.WriteAllTextAsync(filePath, content);
                
                return new EditResult
                {
                    Success = true,
                    ModifiedContent = content
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying edits to file");
                return new EditResult
                {
                    Success = false,
                    ErrorMessage = $"Error applying edits: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Applies a single edit to content
        /// </summary>
        public EditResult ApplyEditToContent(string content, FileEdit edit)
        {
            var analysis = AnalyzeMatches(content, edit.OldText);
            
            // Check if we can proceed with the edit
            if (analysis.IsValid || (edit.ReplaceAll && analysis.ExactMatches.Count > 0))
            {
                string modifiedContent;
                
                if (edit.ReplaceAll)
                {
                    modifiedContent = content.Replace(edit.OldText, edit.NewText);
                }
                else
                {
                    modifiedContent = ReplaceFirst(content, edit.OldText, edit.NewText);
                }
                
                return new EditResult
                {
                    Success = true,
                    ModifiedContent = modifiedContent
                };
            }
            
            // Generate detailed error message
            var errorMessage = GenerateErrorMessage(analysis, edit.OldText);
            
            return new EditResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Diagnostics = analysis.ToDiagnostics()
            };
        }

        /// <summary>
        /// Analyzes all matches for a search pattern
        /// </summary>
        private MatchAnalysis AnalyzeMatches(string content, string searchText)
        {
            var analysis = new MatchAnalysis();
            
            // Phase 1: Find exact matches
            analysis.ExactMatches = FindExactMatches(content, searchText);
            
            if (analysis.ExactMatches.Count == 1)
            {
                analysis.IsValid = true;
                return analysis;
            }
            
            // Phase 2: If no exact matches, look for fuzzy matches
            if (analysis.ExactMatches.Count == 0)
            {
                analysis.WhitespaceMatches = FindWhitespaceMatches(content, searchText);
                analysis.CaseMatches = FindCaseInsensitiveMatches(content, searchText);
                analysis.PartialMatches = FindPartialMatches(content, searchText);
                analysis.FirstLineMatchCount = FindFirstLineMatches(content, searchText);
            }
            
            return analysis;
        }

        /// <summary>
        /// Finds all exact matches in the content
        /// </summary>
        private List<ExactMatch> FindExactMatches(string content, string searchText)
        {
            if (string.IsNullOrEmpty(content))
                return new List<ExactMatch> { new ExactMatch { ColumnNumber = 0, LineNumber = 0, Context = GetContext("", 0, 0), Index = 0 } };
            var matches = new List<ExactMatch>();
            int index = 0;
            
            while ((index = content.IndexOf(searchText, index, StringComparison.Ordinal)) != -1)
            {
                var (line, column) = TextVisualization.GetLineAndColumn(content, index);
                
                matches.Add(new ExactMatch
                {
                    Index = index,
                    LineNumber = line,
                    ColumnNumber = column,
                    Context = GetContext(content, index, searchText.Length)
                });
                
                index += searchText.Length;
            }
            
            return matches;
        }

        /// <summary>
        /// Finds matches that differ only in whitespace
        /// </summary>
        private List<FuzzyMatch> FindWhitespaceMatches(string content, string searchText)
        {
            var matches = new List<FuzzyMatch>();
            var normalizedSearch = NormalizeWhitespace(searchText);
            
            if (string.IsNullOrWhiteSpace(normalizedSearch))
                return matches;
            
            // Create regex pattern that allows flexible whitespace
            var pattern = CreateWhitespaceFlexiblePattern(searchText);
            
            try
            {
                var regexMatches = Regex.Matches(content, pattern, RegexOptions.Singleline);
                
                foreach (Match match in regexMatches)
                {
                    var (line, column) = TextVisualization.GetLineAndColumn(content, match.Index);
                    
                    matches.Add(new FuzzyMatch
                    {
                        LineNumber = line,
                        ColumnNumber = column,
                        ActualText = match.Value,
                        ExpectedText = searchText,
                        Type = MatchType.WhitespaceMismatch,
                        Similarity = CalculateSimilarity(searchText, match.Value),
                        Difference = TextVisualization.IdentifyDifferences(searchText, match.Value)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in whitespace matching");
            }
            
            return matches;
        }

        /// <summary>
        /// Finds matches that differ only in case
        /// </summary>
        private List<FuzzyMatch> FindCaseInsensitiveMatches(string content, string searchText)
        {
            var matches = new List<FuzzyMatch>();
            int index = 0;
            
            while ((index = content.IndexOf(searchText, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                var actualText = content.Substring(index, searchText.Length);
                
                // Skip if it's an exact match (already found)
                if (actualText.Equals(searchText, StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }
                
                var (line, column) = TextVisualization.GetLineAndColumn(content, index);
                
                matches.Add(new FuzzyMatch
                {
                    LineNumber = line,
                    ColumnNumber = column,
                    ActualText = actualText,
                    ExpectedText = searchText,
                    Type = MatchType.CaseMismatch,
                    Similarity = 0.95, // High similarity for case-only differences
                    Difference = $"Case difference: expected '{searchText}' but found '{actualText}'"
                });
                
                index++;
            }
            
            return matches;
        }

        /// <summary>
        /// Finds partial matches using similarity scoring
        /// </summary>
        private List<FuzzyMatch> FindPartialMatches(string content, string searchText)
        {
            var matches = new List<FuzzyMatch>();
            
            // For performance, only check if search text is reasonably sized
            if (searchText.Length > 500)
                return matches;
            
            // Split content into chunks roughly the size of search text
            var chunkSize = searchText.Length;
            var overlap = chunkSize / 2; // 50% overlap to catch boundary matches
            
            for (int i = 0; i < content.Length - chunkSize + 1; i += overlap)
            {
                var chunk = content.Substring(i, Math.Min(chunkSize, content.Length - i));
                var similarity = CalculateSimilarity(searchText, chunk);
                
                if (similarity >= PARTIAL_MATCH_THRESHOLD)
                {
                    var (line, column) = TextVisualization.GetLineAndColumn(content, i);
                    
                    matches.Add(new FuzzyMatch
                    {
                        LineNumber = line,
                        ColumnNumber = column,
                        ActualText = chunk,
                        ExpectedText = searchText,
                        Type = MatchType.PartialMatch,
                        Similarity = similarity,
                        Difference = TextVisualization.IdentifyDifferences(searchText, chunk)
                    });
                }
            }
            
            // Sort by similarity and keep only top matches
            return matches.OrderByDescending(m => m.Similarity).Take(3).ToList();
        }

        /// <summary>
        /// Checks if first line of search text appears in content
        /// </summary>
        private int FindFirstLineMatches(string content, string searchText)
        {
            var lines = searchText.Split('\n');
            if (lines.Length <= 1)
                return 0;
            
            var firstLine = lines[0].TrimEnd('\r');
            return CountOccurrences(content, firstLine);
        }

        /// <summary>
        /// Generates a detailed error message based on match analysis
        /// </summary>
        private string GenerateErrorMessage(MatchAnalysis analysis, string searchText)
        {
            var sb = new StringBuilder();
            
            // Multiple exact matches
            if (analysis.ExactMatches.Count > 1)
            {
                sb.AppendLine($"MULTIPLE MATCHES: Found {analysis.ExactMatches.Count} exact matches");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("Locations:");
                
                foreach (var match in analysis.ExactMatches.Take(5))
                {
                    sb.AppendLine($"  - Line {match.LineNumber}, Column {match.ColumnNumber}");
                    sb.AppendLine($"    {TextVisualization.Preview(match.Context, 60)}");
                }
                
                if (analysis.ExactMatches.Count > 5)
                {
                    sb.AppendLine($"  ... and {analysis.ExactMatches.Count - 5} more");
                }
                
                sb.AppendLine();
                sb.AppendLine("Suggestion:");
                sb.AppendLine("  Provide more context to make it unique, or use ReplaceAll = true");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                
                return sb.ToString();
            }
            
            // No exact matches - check fuzzy matches
            if (analysis.WhitespaceMatches.Any())
            {
                var match = analysis.WhitespaceMatches.First();
                sb.AppendLine("WHITESPACE MISMATCH: Found matching code with different spacing");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine(TextVisualization.CreateComparison(match.ExpectedText, match.ActualText, true));
                sb.AppendLine();
                sb.AppendLine("Difference:");
                sb.AppendLine($"  {match.Difference}");
                sb.AppendLine();
                sb.AppendLine("Suggestion:");
                sb.AppendLine("  Copy the exact formatting from the file, including all spaces, tabs, and newlines");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                
                return sb.ToString();
            }
            
            if (analysis.CaseMatches.Any())
            {
                var match = analysis.CaseMatches.First();
                sb.AppendLine("CASE MISMATCH: Found matching text with different case");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine(TextVisualization.CreateComparison(match.ExpectedText, match.ActualText, false));
                sb.AppendLine();
                sb.AppendLine("Suggestion:");
                sb.AppendLine($"  Use the correct case: '{match.ActualText}'");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                
                return sb.ToString();
            }
            
            if (analysis.PartialMatches.Any())
            {
                var match = analysis.PartialMatches.First();
                sb.AppendLine($"PARTIAL MATCH: Found similar text (similarity: {match.Similarity:P0})");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine(TextVisualization.CreateComparison(match.ExpectedText, match.ActualText, false));
                sb.AppendLine();
                sb.AppendLine("Difference:");
                sb.AppendLine($"  {match.Difference}");
                sb.AppendLine();
                sb.AppendLine("Suggestion:");
                sb.AppendLine("  Check for typos or minor differences and use the actual text from the file");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                
                return sb.ToString();
            }
            
            if (analysis.FirstLineMatchCount > 0)
            {
                var firstLine = searchText.Split('\n')[0];
                sb.AppendLine($"PARTIAL CONTEXT: First line found {analysis.FirstLineMatchCount} time(s), but full text not found");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"First line: {TextVisualization.Preview(firstLine, 80)}");
                sb.AppendLine();
                sb.AppendLine("This suggests the code exists but has been modified.");
                sb.AppendLine();
                sb.AppendLine("Suggestion:");
                sb.AppendLine("  Review the current file content and update your search text");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                
                return sb.ToString();
            }
            
            // No matches at all
            sb.AppendLine("NO MATCH: Text not found in file");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"Searched for:");
            sb.AppendLine($"  {TextVisualization.Preview(searchText, 150)}");
            sb.AppendLine();
            sb.AppendLine("Suggestion:");
            sb.AppendLine("  Verify the text exists in the file and check for typos");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            return sb.ToString();
        }

        #region Helper Methods

        private string ReplaceFirst(string text, string oldValue, string newValue)
        {
            int index = text.IndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0)
                return text;
            
            return text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
        }

        private int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            
            return count;
        }

        private string NormalizeWhitespace(string text)
        {
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private string CreateWhitespaceFlexiblePattern(string searchText)
        {
            // Escape special regex characters
            var escaped = Regex.Escape(searchText);
            
            // Replace escaped whitespace with flexible pattern
            escaped = Regex.Replace(escaped, @"\\s+", @"\s+");
            escaped = Regex.Replace(escaped, @"\\ ", @"\s+");
            escaped = Regex.Replace(escaped, @"\\t", @"\s+");
            escaped = Regex.Replace(escaped, @"\\r\\n", @"\s+");
            escaped = Regex.Replace(escaped, @"\\n", @"\s+");
            
            return escaped;
        }

        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;
            
            if (s1.Equals(s2))
                return 1;
            
            int maxLength = Math.Max(s1.Length, s2.Length);
            int distance = LevenshteinDistance(s1, s2);
            
            return 1.0 - (double)distance / maxLength;
        }

        private int LevenshteinDistance(string s1, string s2)
        {
            var distances = new int[s1.Length + 1, s2.Length + 1];
            
            for (int i = 0; i <= s1.Length; i++)
                distances[i, 0] = i;
            
            for (int j = 0; j <= s2.Length; j++)
                distances[0, j] = j;
            
            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + cost
                    );
                }
            }
            
            return distances[s1.Length, s2.Length];
        }

        private string GetContext(string content, int index, int length)
        {
            var contextBefore = 30;
            var contextAfter = 30;
            
            var start = Math.Max(0, index - contextBefore);
            var end = Math.Min(content.Length, index + length + contextAfter);
            
            var context = content.Substring(start, end - start);
            
            // Replace newlines with spaces for display
            return context.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        }

        #endregion
    }
}