using AiStudio4.Tools.Models;
using AiStudio4.Tools.Interfaces;
using AiStudio4.Tools.Services.SmartFileEditor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiStudio4.Tools.AzureDevOps
{
    /// <summary>
    /// Tool for creating or updating Azure DevOps wiki pages using local git repository operations.
    /// This approach is more efficient than REST API calls for partial updates and batch operations.
    /// </summary>
    [McpServerToolType]
    public class AzureDevOpsCreateOrUpdateWikiPageViaLocalTool : BaseToolImplementation
    {
        private readonly ISmartFileEditor? _smartFileEditor;

        public AzureDevOpsCreateOrUpdateWikiPageViaLocalTool(
            ILogger<AzureDevOpsCreateOrUpdateWikiPageViaLocalTool>? logger = null,
            IGeneralSettingsService? generalSettingsService = null,
            IStatusMessageService? statusMessageService = null,
            ISmartFileEditor? smartFileEditor = null)
            : base(logger, generalSettingsService, statusMessageService)
        {
            _smartFileEditor = smartFileEditor;
        }

        public override Tool GetToolDefinition()
        {
            return new Tool
            {
                Guid = ToolGuids.AZURE_DEV_OPS_CREATE_OR_UPDATE_WIKI_PAGE_VIA_LOCAL_TOOL_GUID,
                Name = "AzureDevOpsCreateOrUpdateWikiPageViaLocal",
                Description = """
Create or update Azure DevOps wiki pages using local git repository operations for efficient partial updates.

This tool clones the wiki repository locally and performs git operations directly, which is more efficient than REST API calls for:
- Small edits to large pages
- Batch updates across multiple pages
- Maintaining change history locally

The tool automatically handles:
- Repository cloning and authentication using PAT
- Atomic commits and pushes for every modification
- Merge conflict detection
- Rollback on failure
""",
                Categories = new List<string> { "Azure DevOps", "Wiki", "Git" },
                Schema = """
{
  "name": "AzureDevOpsCreateOrUpdateWikiPageViaLocal",
  "description": "Create or update Azure DevOps wiki pages using local git repository operations for efficient partial updates",
  "input_schema": {
    "type": "object",
    "properties": {
      "organization": { 
        "type": "string", 
        "description": "Azure DevOps organization name" 
      },
      "project": { 
        "type": "string", 
        "description": "Azure DevOps project name" 
      },
      "wiki_id": { 
        "type": "string", 
        "description": "ID of the wiki" 
      },
      "path": { 
        "type": "string", 
        "description": "Path to the wiki page (e.g., '/Home', '/Documentation/API')" 
      },
      "changes": {
        "type": "array",
        "description": "List of changes to apply to the wiki page",
        "items": {
          "type": "object",
          "properties": {
            "oldContent": { 
              "type": "string", 
              "description": "Content to find and replace (empty string for append)" 
            },
            "newContent": { 
              "type": "string", 
              "description": "New content to replace with" 
            },
            "description": { 
              "type": "string", 
              "description": "Description of this change" 
            }
          },
          "required": ["oldContent", "newContent"]
        }
      },
      "comment": { 
        "type": "string", 
        "description": "Commit message (optional)" 
      },
      "auto_pull": { 
        "type": "boolean", 
        "description": "Automatically pull latest changes before editing (default: true)" 
      }
    },
    "required": ["organization", "project", "wiki_id", "path", "changes"]
  }
}
"""
            };
        }

        public override async Task<BuiltinToolResult> ProcessAsync(string arguments, Dictionary<string, string> environmentData)
        {
            try
            {
                SendStatusUpdate("Parsing parameters...");
                var parameters = ParseParameters(arguments);

                // Validate PAT is available
                var pat = _generalSettingsService.GetDecryptedAzureDevOpsPAT();
                if (string.IsNullOrEmpty(parameters.WikiId))
                {
                    return new BuiltinToolResult
                    {
                        WasProcessed = true,
                        ContinueProcessing = true,
                        ResultMessage = "Error: wiki_id is required."
                    };
                }

                if (string.IsNullOrEmpty(pat))
                {
                    return new BuiltinToolResult
                    {
                        WasProcessed = true,
                        ContinueProcessing = true,
                        ResultMessage = "Error: Azure DevOps PAT is not configured. Please set it in the settings."
                    };
                }

                // Ensure repository exists and is up to date
                SendStatusUpdate("Setting up local wiki repository...");
                var repoPath = await EnsureRepositoryExists(parameters.Organization, parameters.Project, parameters.WikiId, pat, parameters.AutoPull);
                
                if (string.IsNullOrEmpty(repoPath))
                {
                    return new BuiltinToolResult
                    {
                        WasProcessed = true,
                        ContinueProcessing = true,
                        ResultMessage = "Error: Failed to set up local wiki repository"
                    };
                }

                // Construct the file path for the wiki page
                var wikiFilePath = GetWikiFilePath(repoPath, parameters.Path);
                
                // Ensure the file exists (create if new)
                if (!File.Exists(wikiFilePath))
                {
                    SendStatusUpdate($"Creating new wiki page at {parameters.Path}...");
                    var directory = Path.GetDirectoryName(wikiFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    await File.WriteAllTextAsync(wikiFilePath, string.Empty);
                }

                // Apply changes using ModifyFileModernTool pattern
                SendStatusUpdate("Applying changes to wiki page...");
                var modifyResult = await ApplyChangesToFile(wikiFilePath, parameters.Changes);
                
                if (!modifyResult.Success)
                {
                    return new BuiltinToolResult
                    {
                        WasProcessed = true,
                        ContinueProcessing = true,
                        ResultMessage = $"Error: {modifyResult.ErrorMessage}"
                    };
                }

                // Commit and push changes
                SendStatusUpdate("Committing and pushing changes...");
                var commitMessage = parameters.Comment ?? GenerateCommitMessage(parameters.Path, parameters.Changes);
                var pushResult = await CommitAndPush(repoPath, wikiFilePath, commitMessage, pat);
                
                if (!pushResult.Success)
                {
                    // Attempt to revert changes if push failed
                    await RevertChanges(repoPath);
                    return new BuiltinToolResult
                    {
                        WasProcessed = true,
                        ContinueProcessing = true,
                        ResultMessage = $"Error: Failed to push changes: {pushResult.ErrorMessage}"
                    };
                }

                SendStatusUpdate("Wiki page updated successfully!");
                return new BuiltinToolResult
                {
                    WasProcessed = true,
                    ContinueProcessing = true,
                    ResultMessage = $"Successfully updated wiki page '{parameters.Path}' in {parameters.Organization}/{parameters.Project}/{parameters.WikiId}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing AzureDevOpsCreateOrUpdateWikiPageViaLocalTool");
                return new BuiltinToolResult
                {
                    WasProcessed = true,
                    ContinueProcessing = true,
                    ResultMessage = $"Error: {ex.Message}"
                };
            }
        }

        private async Task<string> EnsureRepositoryExists(string organization, string project, string wikiId, string pat, bool autoPull)
        {
            try
            {
                // TODO: PathHelper needs to be migrated or replaced with a different approach
                // var wikisPath = PathHelper.GetProfileSubPath("wikis", organization, project, wikiName);
                var wikisPath = Path.Combine(Path.GetTempPath(), "wikis", organization, project, wikiId);
                
                if (!Directory.Exists(wikisPath))
                {
                    // Clone the repository
                    SendStatusUpdate($"Cloning wiki repository {wikiId}...");
                    var cloneUrl = $"https://{pat}@dev.azure.com/{organization}/{project}/_git/{wikiId}";
                    
                    var parentDir = Path.GetDirectoryName(wikisPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    var cloneResult = await RunGitCommand("clone", $"\"{cloneUrl}\" \"{wikisPath}\"", parentDir);
                    if (!cloneResult.Success)
                    {
                        _logger.LogError($"Failed to clone repository: {cloneResult.Error}");
                        return null;
                    }
                }
                else if (autoPull)
                {
                    // Pull latest changes
                    SendStatusUpdate("Pulling latest changes...");
                    var pullResult = await RunGitCommand("pull", "", wikisPath);
                    if (!pullResult.Success && !pullResult.Error.Contains("Already up to date"))
                    {
                        _logger.LogError($"Failed to pull latest changes: {pullResult.Error}");
                        // Don't fail if pull fails - might have local changes
                    }
                }

                return wikisPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring repository exists");
                return null;
            }
        }

        private string GetWikiFilePath(string repoPath, string wikiPath)
        {
            // Remove leading slash if present
            if (wikiPath.StartsWith("/"))
            {
                wikiPath = wikiPath.Substring(1);
            }

            // Replace forward slashes with system path separator
            wikiPath = wikiPath.Replace('/', Path.DirectorySeparatorChar);

            // Add .md extension if not present
            if (!wikiPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                wikiPath += ".md";
            }

            return Path.Combine(repoPath, wikiPath);
        }

        private async Task<(bool Success, string ErrorMessage)> ApplyChangesWithSmartEditor(ISmartFileEditor smartEditor, string filePath, List<WikiChange> changes)
        {
            try
            {
                // Convert WikiChange to FileEdit
                var edits = changes.Select(c => new FileEdit
                {
                    OldText = c.OldContent,
                    NewText = c.NewContent,
                    ReplaceAll = false,
                    Description = c.Description
                }).ToList();

                var result = await smartEditor.ApplyEditsAsync(filePath, edits);
                
                if (!result.Success)
                {
                    _logger.LogWarning($"SmartFileEditor failed with detailed error: {result.ErrorMessage}");
                }
                
                return (result.Success, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error using SmartFileEditor");
                return (false, $"Error using SmartFileEditor: {ex.Message}");
            }
        }

        private async Task<(bool Success, string ErrorMessage)> ApplyChangesToFile(string filePath, List<WikiChange> changes)
        {
            try
            {
                // Try to use SmartFileEditor first
                if (_smartFileEditor != null)
                {
                    return await ApplyChangesWithSmartEditor(_smartFileEditor, filePath, changes);
                }
                
                // Fallback to manual file modification if SmartFileEditor not available
                _logger.LogWarning("SmartFileEditor not available, falling back to manual file modification");
                return await ApplyChangesManually(filePath, changes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying changes to file");
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string ErrorMessage)> ApplyChangesManually(string filePath, List<WikiChange> changes)
        {
            try
            {
                // Read the file
                var content = await File.ReadAllTextAsync(filePath);
                var originalContent = content;

                // Apply each change
                foreach (var change in changes)
                {
                    if (string.IsNullOrEmpty(change.OldContent))
                    {
                        // If oldContent is empty, append newContent
                        content = content + change.NewContent;
                    }
                    else
                    {
                        // Replace oldContent with newContent
                        if (!content.Contains(change.OldContent))
                        {
                            return (false, $"Could not find content to replace: {change.OldContent.Substring(0, Math.Min(50, change.OldContent.Length))}...");
                        }
                        content = content.Replace(change.OldContent, change.NewContent);
                    }
                }

                // Write the file back
                await File.WriteAllTextAsync(filePath, content);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to apply changes manually: {ex.Message}");
            }
        }

        private async Task<(bool Success, string ErrorMessage)> CommitAndPush(string repoPath, string filePath, string commitMessage, string pat)
        {
            try
            {
                // Stage the file
                var relativePath = Path.GetRelativePath(repoPath, filePath);
                var addResult = await RunGitCommand("add", $"\"{relativePath}\"", repoPath);
                if (!addResult.Success)
                {
                    return (false, $"Failed to stage file: {addResult.Error}");
                }

                // Commit changes
                var commitResult = await RunGitCommand("commit", $"-m \"{commitMessage}\"", repoPath);
                if (!commitResult.Success)
                {
                    if (commitResult.Output.Contains("nothing to commit") || commitResult.Error.Contains("nothing to commit"))
                    {
                        return (true, "No changes to commit");
                    }
                    return (false, $"Failed to commit: {commitResult.Error}");
                }

                // Push changes with authentication
                var pushResult = await RunGitCommandWithAuth("push", "", repoPath, pat);
                if (!pushResult.Success)
                {
                    return (false, $"Failed to push: {pushResult.Error}");
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during commit and push");
                return (false, ex.Message);
            }
        }

        private async Task RevertChanges(string repoPath)
        {
            try
            {
                SendStatusUpdate("Reverting local changes...");
                await RunGitCommand("reset", "--hard HEAD", repoPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reverting changes");
            }
        }

        private async Task<(bool Success, string Output, string Error)> RunGitCommand(string command, string arguments, string workingDirectory)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"{command} {arguments}",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return (false, "", "Failed to start git process");
                }

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                        outputBuilder.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                        errorBuilder.AppendLine(e.Data);
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();
                var success = process.ExitCode == 0;

                _logger.LogDebug($"Git command: {command} {arguments}\nOutput: {output}\nError: {error}\nExit code: {process.ExitCode}");

                return (success, output, error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error running git command: {command} {arguments}");
                return (false, "", ex.Message);
            }
        }

        private async Task<(bool Success, string Output, string Error)> RunGitCommandWithAuth(string command, string arguments, string workingDirectory, string pat)
        {
            // For push operations, we need to ensure the PAT is used for authentication
            // We'll temporarily set the credential helper to use the PAT
            try
            {
                // First, get the remote URL
                var remoteResult = await RunGitCommand("remote", "get-url origin", workingDirectory);
                if (!remoteResult.Success)
                {
                    return (false, "", "Failed to get remote URL");
                }

                var remoteUrl = remoteResult.Output.Trim();
                
                // If the URL doesn't contain the PAT, update it temporarily
                if (!remoteUrl.Contains("@dev.azure.com"))
                {
                    // URL is not authenticated, need to add PAT
                    var organization = ExtractOrganizationFromPath(workingDirectory);
                    var project = ExtractProjectFromPath(workingDirectory);
                    var wikiName = ExtractWikiNameFromPath(workingDirectory);
                    
                    remoteUrl = $"https://{pat}@dev.azure.com/{organization}/{project}/_git/{wikiName}.wiki";
                }
                else if (!remoteUrl.StartsWith($"https://{pat}@"))
                {
                    // URL has different auth, update it
                    var urlParts = remoteUrl.Split('@');
                    if (urlParts.Length > 1)
                    {
                        remoteUrl = $"https://{pat}@{urlParts[1]}";
                    }
                }

                // Run the command with the authenticated URL
                return await RunGitCommand(command, $"{arguments} {remoteUrl}", workingDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running authenticated git command");
                return (false, "", ex.Message);
            }
        }

        private string ExtractOrganizationFromPath(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar);
            var wikisIndex = Array.IndexOf(parts, "wikis");
            return wikisIndex >= 0 && wikisIndex + 1 < parts.Length ? parts[wikisIndex + 1] : "";
        }

        private string ExtractProjectFromPath(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar);
            var wikisIndex = Array.IndexOf(parts, "wikis");
            return wikisIndex >= 0 && wikisIndex + 2 < parts.Length ? parts[wikisIndex + 2] : "";
        }

        private string ExtractWikiNameFromPath(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar);
            var wikisIndex = Array.IndexOf(parts, "wikis");
            return wikisIndex >= 0 && wikisIndex + 3 < parts.Length ? parts[wikisIndex + 3] : "";
        }

        private string GenerateCommitMessage(string path, List<WikiChange> changes)
        {
            if (changes.Count == 1)
            {
                return $"Update {path}: {changes[0].Description ?? "content change"}";
            }
            return $"Update {path}: {changes.Count} changes";
        }

        private WikiParameters ParseParameters(string arguments)
        {
            dynamic json = JsonConvert.DeserializeObject(arguments);
            
            var parameters = new WikiParameters
            {
                Organization = json.organization,
                Project = json.project,
                WikiId = json.wiki_id ?? json.wiki_name,
                Path = json.path,
                Comment = json.comment,
                AutoPull = json.auto_pull ?? true,
                Changes = new List<WikiChange>()
            };

            if (json.changes != null)
            {
                foreach (var change in json.changes)
                {
                    parameters.Changes.Add(new WikiChange
                    {
                        OldContent = change.oldContent,
                        NewContent = change.newContent,
                        Description = change.description
                    });
                }
            }

            return parameters;
        }

        private class WikiParameters
        {
            public string Organization { get; set; }
            public string Project { get; set; }
            public string WikiId { get; set; }
            public string Path { get; set; }
            public string Comment { get; set; }
            public bool AutoPull { get; set; }
            public List<WikiChange> Changes { get; set; }
        }

        private class WikiChange
        {
            public string OldContent { get; set; }
            public string NewContent { get; set; }
            public string Description { get; set; }
        }

        // Public class for MCP parameter binding
        public class WikiChangeInput
        {
            [Description("Content to find and replace (empty string for append)")]
            public string oldContent { get; set; } = "";
            
            [Description("New content to replace with")]
            [Required]
            public string newContent { get; set; } = "";
            
            [Description("Description of this change")]
            public string? description { get; set; }
        }

        [McpServerTool, Description("Create or update Azure DevOps wiki pages using local git repository operations for efficient partial updates")]
        public async Task<string> AzureDevOpsCreateOrUpdateWikiPageViaLocal(
            [Description("Azure DevOps organization name"), Required] string organization,
            [Description("Azure DevOps project name"), Required] string project,
            [Description("The ID or name of the wiki"), Required] string wiki_id,
            [Description("Path to the wiki page (e.g., '/Home', '/Documentation/API')"), Required] string path,
            [Description("List of changes to apply to the wiki page"), Required] WikiChangeInput[] changes,
            [Description("Commit message (optional)")] string? comment = null,
            [Description("Automatically pull latest changes before editing (default: true)")] bool auto_pull = true)
        {
            // Convert WikiChangeInput[] to the format ProcessAsync expects
            var changesForProcessing = changes.Select(c => new 
            {
                oldContent = c.oldContent,
                newContent = c.newContent,
                description = c.description
            }).ToArray();
            
            // Build the JSON parameters object that ProcessAsync expects
            // Note: ProcessAsync expects 'wiki_name' internally, so we map wiki_id to wiki_name
            var parametersObj = new
            {
                organization = organization,
                project = project,
                wiki_name = wiki_id,  // Map wiki_id to wiki_name for internal processing
                path = path,
                changes = changesForProcessing,
                comment = comment,
                auto_pull = auto_pull
            };
            
            string jsonParameters = JsonConvert.SerializeObject(parametersObj);
            return await ExecuteWithExtraProperties(jsonParameters);
        }
    }
}