using AiStudio4.Dialogs; 
 
using AiStudio4.Services;
using AiStudio4.InjectedDependencies;

using AiStudio4.Core.Tools.CodeDiff.FileOperationHandlers;
using AiStudio4.Core.Tools.CodeDiff.Models;
using AiStudio4.Services.Interfaces; 
using Microsoft.Extensions.DependencyInjection;

using Microsoft.Win32; 


using AiStudio4.Dialogs; 

using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;




using AiStudio4.Core.Services;
using static RoslynHelper;
 


using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace AiStudio4;

public partial class WebViewWindow : Window
{
    private readonly WindowManager _windowManager;
    private readonly IMcpService _mcpService;
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly IAppearanceSettingsService _appearanceSettingsService;
    private readonly IBuiltinToolService _builtinToolService;
    private readonly IWebSocketNotificationService _notificationService;
    private readonly IProjectPackager _projectPackager;
    private readonly ILogger<WebViewWindow> _logger;
    private readonly IDotNetProjectAnalyzerService _dotNetProjectAnalyzerService;
    private readonly IProjectFileWatcherService _projectFileWatcherService;
    private readonly IGoogleDriveService _googleDriveService;
    private readonly IConvStorage _convStorage;
    private readonly IUpdateNotificationService _updateNotificationService;
    private readonly ISystemPromptService _systemPromptService;
    private readonly IProjectService _projectService;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _licensesJsonPath;
    private readonly string _nugetLicense1Path;
    private readonly string _nugetLicense2Path;
    private string _lastTranscriptionResult = null;

    public WebViewWindow(WindowManager windowManager,
                         IMcpService mcpService,
                         IGeneralSettingsService generalSettingsService,
                         IAppearanceSettingsService appearanceSettingsService,
                         IBuiltinToolService builtinToolService,
                         IWebSocketNotificationService notificationService,
                         IProjectPackager projectPackager,
                         IDotNetProjectAnalyzerService dotNetProjectAnalyzerService,
                         IProjectFileWatcherService projectFileWatcherService,
                         ILogger<WebViewWindow> logger,
                         IConvStorage convStorage,
                         IGoogleDriveService googleDriveService,
                         IUpdateNotificationService updateNotificationService,
                         ISystemPromptService systemPromptService,
                         IProjectService projectService,
                         IServiceProvider serviceProvider)
    {
        _windowManager = windowManager;
        _mcpService = mcpService;
        _generalSettingsService = generalSettingsService;
        _appearanceSettingsService = appearanceSettingsService;
        _builtinToolService = builtinToolService;
        _notificationService = notificationService;
        _projectPackager = projectPackager;
        _convStorage = convStorage; 
        _logger = logger;
        _dotNetProjectAnalyzerService = dotNetProjectAnalyzerService;
        _projectFileWatcherService = projectFileWatcherService;
        _googleDriveService = googleDriveService;
        _updateNotificationService = updateNotificationService;
        _systemPromptService = systemPromptService;
        _projectService = projectService;
        _serviceProvider = serviceProvider;

        
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _licensesJsonPath = Path.Combine(baseDir, "AiStudioClient", "dist", "licenses.txt");
        _nugetLicense1Path = Path.Combine(baseDir, "app-nuget-license.txt");
        _nugetLicense2Path = Path.Combine(baseDir, "sharedclasses-nuget-license.txt");
        
        InitializeComponent();
        UpdateWindowTitle(); 
        UpdateAllowConnectionsOutsideLocalhostMenuItem(); 
        UpdateUpdateAvailableMenuItem();
        UpdateWikiSyncMenuItems();
        webView.Initialize(_generalSettingsService.CurrentSettings.AllowConnectionsOutsideLocalhost);
        _generalSettingsService.SettingsChanged += OnGeneralSettingsChanged;

        // if (System.Diagnostics.Debugger.IsAttached)
    }

    private async void ImportFromGoogleDriveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImportFromGoogleDriveMenuItem.IsEnabled = false; 
        try
        {
            Debug.WriteLine("[UI] Clicked Import from Google Drive.");

            
            string currentWebSocketClientId = null;
            if (webView.CoreWebView2 != null) 
            {
                try
                {
                    
                    string jsResult = await webView.CoreWebView2.ExecuteScriptAsync("window.webSocketService ? window.webSocketService.getClientId() : null;");
                    if (jsResult != null && jsResult != "null" && jsResult != "\"null\"") 
                    {
                        currentWebSocketClientId = JsonConvert.DeserializeObject<string>(jsResult);
                        _logger.LogInformation("Obtained clientId from WebView2: {ClientId}", currentWebSocketClientId);
                    }
                    else
                    {
                        _logger.LogWarning("ClientId from WebView2 was null or 'null'.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get clientId from WebView2 for import notification.");
                    
                }
            }
            else
            {
                _logger.LogWarning("CoreWebView2 not initialized when trying to get clientId.");
            }

            
            var fileList = await _googleDriveService.ListFilesFromAiStudioFolderAsync();

            if (fileList == null) 
            {
                Debug.WriteLine("[UI] Failed to retrieve file list from Google Drive. See logs/previous messages.");
                MessageBox.Show("Could not connect to Google Drive or an error occurred. Please check the application logs. Ensure you have authorized AiStudio4 and have a 'credentials.json' file.", "Google Drive Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!fileList.Any())
            {
                Debug.WriteLine("[UI] No files found in the 'Google AI Studio' folder.");
                MessageBox.Show("No files found in your 'Google AI Studio' folder on Google Drive.", "No Files Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            
            var dialog = new GoogleDriveFileSelectionDialog(fileList)
            {
                Owner = this 
            };

            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true || !dialog.SelectedFiles.Any())
            {
                Debug.WriteLine("[UI] Google Drive file selection cancelled or no files selected.");
                MessageBox.Show("No files were selected for import.", "Import Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var importedConversations = new List<v4BranchedConv>();
            var firstImportedConvId = string.Empty;

            foreach (var fileToImport in dialog.SelectedFiles)
            {
                Debug.WriteLine($"[UI] Downloading file: {fileToImport.Name} (ID: {fileToImport.Id})");

                (string,string) fileContent;
                try
                {
                    fileContent = await _googleDriveService.DownloadFileContentAsync(fileToImport.Id);
                    Debug.WriteLine($"[UI] Successfully downloaded file content for {fileToImport.Name}, size: {fileContent.Item2.Length} characters");
                }
                catch (Exception downloadEx)
                {
                    Debug.WriteLine($"[UI] Error downloading file content for {fileToImport.Name}: {downloadEx.Message}");
                    _logger.LogError(downloadEx, "Error downloading file content for {FileName}", fileToImport.Name);
                    MessageBox.Show($"Error downloading file content for '{fileToImport.Name}': {downloadEx.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    continue; 
                }

                
                v4BranchedConv convertedConv;
                try
                {
                    convertedConv = await GoogleAiStudioConverter.ConvertToAiStudio4Async(fileContent.Item2, fileContent.Item1, _googleDriveService);
                    Debug.WriteLine($"[UI] Successfully converted conversation. ConvId: {convertedConv.ConvId}, Messages: {convertedConv.Messages.Count}");
                }
                catch (Exception convertEx)
                {
                    Debug.WriteLine($"[UI] Error converting Google AI Studio format for {fileToImport.Name}: {convertEx.Message}");
                    _logger.LogError(convertEx, "Error converting Google AI Studio format for {FileName}", fileToImport.Name);
                    MessageBox.Show($"Error converting Google AI Studio format for '{fileToImport.Name}': {convertEx.Message}", "Conversion Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    continue; 
                }

                
                var messageSelectionDialog = new MessageSelectionDialog(convertedConv)
                {
                    Owner = this
                };

                bool? messageDialogResult = messageSelectionDialog.ShowDialog();
                if (messageDialogResult != true || !messageSelectionDialog.SelectedMessages.Any())
                {
                    Debug.WriteLine($"[UI] Message selection cancelled or no messages selected for {fileToImport.Name}.");
                    continue; 
                }

                
                var importedConv = new v4BranchedConv
                {
                    ConvId = convertedConv.ConvId,
                    Summary = convertedConv.Summary,
                    SystemPromptId = convertedConv.SystemPromptId,
                    Messages = new List<v4BranchedConvMessage>()
                };

                
                var systemMessage = convertedConv.Messages.FirstOrDefault(m => m.Role == v4BranchedConvMessageRole.System);
                if (systemMessage != null)
                {
                    importedConv.Messages.Add(systemMessage.Clone());
                }

                
                foreach (var selectedMessage in messageSelectionDialog.SelectedMessages)
                {
                    importedConv.Messages.Add(selectedMessage.Clone());
                }

                
                RebuildMessageRelationships(importedConv.Messages);

                importedConversations.Add(importedConv);
                if (string.IsNullOrEmpty(firstImportedConvId))
                {
                    firstImportedConvId = importedConv.ConvId;
                }

                Debug.WriteLine($"[UI] Created filtered conversation with {importedConv.Messages.Count} messages (including system message).");

                
                try
                {
                    if (_convStorage == null)
                    {
                        throw new InvalidOperationException("IConvStorage service not available");
                    }
                    
                    await _convStorage.SaveConv(importedConv);
                    Debug.WriteLine($"[UI] Successfully saved imported conversation: {importedConv.ConvId}");
                }
                catch (Exception saveEx)
                {
                    Debug.WriteLine($"[UI] Error saving imported conversation: {saveEx.Message}");
                    _logger.LogError(saveEx, "Error saving imported conversation {ConvId}", importedConv.ConvId);
                    MessageBox.Show($"Error saving imported conversation '{importedConv.Summary}': {saveEx.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    continue; 
                }

                
                try
                {
                    var messagesForListDto = importedConv.Messages.Select(m => new
                    {
                        id = m.Id,
                        contentBlocks = m.ContentBlocks,
                        parentId = m.ParentId,
                        source = m.Role == v4BranchedConvMessageRole.User ? "user" :
                                m.Role == v4BranchedConvMessageRole.Assistant ? "ai" : "system",
                        costInfo = m.CostInfo,
                        attachments = m.Attachments,
                        timestamp = new DateTimeOffset(m.Timestamp).ToUnixTimeMilliseconds(),
                        durationMs = m.DurationMs,
                        cumulativeCost = m.CumulativeCost,
                        temperature = m.Temperature
                    }).ToList();

                    await _notificationService.NotifyConvList(new ConvListDto
                    {
                        ConvId = importedConv.ConvId,
                        Summary = importedConv.Summary,
                        LastModified = DateTime.UtcNow.ToString("o"), 
                        FlatMessageStructure = messagesForListDto
                    });

                    Debug.WriteLine($"[UI] Successfully notified all clients about new conversation: {importedConv.ConvId}");
                }
                catch (Exception notifyEx)
                {
                    Debug.WriteLine($"[UI] Error notifying clients about conversation list update for {importedConv.ConvId}: {notifyEx.Message}");
                    _logger.LogError(notifyEx, "Failed to notify clients about conversation list update for {ConvId}", importedConv.ConvId);
                }
            }

            if (!importedConversations.Any())
            {
                MessageBox.Show("No conversations were successfully imported.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            
            if (!string.IsNullOrEmpty(currentWebSocketClientId) && !string.IsNullOrEmpty(firstImportedConvId))
            {
                var firstConvToLoad = importedConversations.First(c => c.ConvId == firstImportedConvId);
                try
                {
                    var fullMessagesForLoad = firstConvToLoad.Messages.Select(m => new
                    {
                        id = m.Id,
                        contentBlocks = m.ContentBlocks ?? new List<ContentBlock>(),
                        parentId = m.ParentId,
                        source = m.Role == v4BranchedConvMessageRole.User ? "user" :
                                m.Role == v4BranchedConvMessageRole.Assistant ? "ai" : "system",
                        costInfo = m.CostInfo,
                        attachments = m.Attachments,
                        timestamp = new DateTimeOffset(m.Timestamp).ToUnixTimeMilliseconds(),
                        durationMs = m.DurationMs,
                        cumulativeCost = m.CumulativeCost,
                        temperature = m.Temperature
                    }).ToList();

                    var loadConvPayload = new
                    {
                        convId = firstConvToLoad.ConvId,
                        messages = fullMessagesForLoad, 
                        summary = firstConvToLoad.Summary 
                    };

                    await _notificationService.NotifyConvUpdate(currentWebSocketClientId, new ConvUpdateDto
                    {
                        ConvId = firstConvToLoad.ConvId, 
                        MessageId = null, 
                        //Content = loadConvPayload, 
                        Source = "system_import", 
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });

                    Debug.WriteLine($"[UI] Successfully notified initiating client {currentWebSocketClientId} to load conversation {firstConvToLoad.ConvId}");
                }
                catch (Exception loadNotifyEx)
                {
                    Debug.WriteLine($"[UI] Error notifying initiating client to load conversation {firstConvToLoad.ConvId}: {loadNotifyEx.Message}");
                    _logger.LogError(loadNotifyEx, "Failed to notify initiating client to load conversation {ConvId}", firstConvToLoad.ConvId);
                }
            }

            
            string successMessage;
            if (importedConversations.Count == 1)
            {
                successMessage = $"Successfully imported conversation '{importedConversations.First().Summary}' from Google AI Studio.";
            }
            else
            {
                successMessage = $"Successfully imported {importedConversations.Count} conversations from Google AI Studio.";
            }

            if (!string.IsNullOrEmpty(currentWebSocketClientId))
            {
                successMessage += "\n\nThe imported conversation(s) should now be visible and the first one automatically loaded in your chat view.";
            }
            else
            {
                successMessage += "\n\nThe imported conversation(s) are now available in your conversation list. You may need to refresh or click on them to view.";
            }

            MessageBox.Show(successMessage, "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (FileNotFoundException fnfEx) 
        {
            Debug.WriteLine($"[UI] Google Drive credentials error: {fnfEx.Message}");
            MessageBox.Show(fnfEx.Message, "Google Drive Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UI] An unexpected error occurred: {ex.Message}");
            MessageBox.Show($"An unexpected error occurred while trying to import from Google Drive: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportFromGoogleDriveMenuItem.IsEnabled = true; 
        }
    }
    private void OnGeneralSettingsChanged(object sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            
            UpdateWindowTitle();
            UpdateAllowConnectionsOutsideLocalhostMenuItem();
            UpdateWikiSyncMenuItems();
            
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_generalSettingsService != null)
        {
            _generalSettingsService.SettingsChanged -= OnGeneralSettingsChanged;
        }
        base.OnClosed(e);
    }

    private void UpdateWindowTitle()
    {
        
        var projectPathDisplay = string.IsNullOrWhiteSpace(_generalSettingsService.CurrentSettings.ProjectPath)
            ? "[Project Path Not Set]"
            : _generalSettingsService.CurrentSettings.ProjectPath;

        this.Title = $"AiStudio4 v{App.VersionNumber} - {projectPathDisplay}{(Debugger.IsAttached ? " - ⚠️⚠️⚠️ Debugger Attached ⚠️⚠️⚠️" : "")}";
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
    
    private void ExploreProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string projectPath = _generalSettingsService.CurrentSettings.ProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                MessageBox.Show("Project path is not set.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Directory.Exists(projectPath))
            {
                Process.Start("explorer.exe", projectPath);
            }
            else
            {
                MessageBox.Show($"Project directory does not exist: {projectPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening project folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }    private void ViewLogMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logViewerWindow = _serviceProvider.GetRequiredService<LogViewerWindow>();
            logViewerWindow.Owner = this;
            logViewerWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Log Viewer window.");
            MessageBox.Show($"Could not open the Log Viewer window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetCondaPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string currentKey = _generalSettingsService.CurrentSettings.CondaPath ?? string.Empty;
        string prompt = "Enter conda path here, eg C:\\Users\\username\\miniconda3\\Scripts";
        string title = "Set conda path";

        var dialog = new WpfInputDialog(title, prompt, currentKey)
        {
            Owner = this 
        };

        if (dialog.ShowDialog() == true)
        {
            string newKey = dialog.ResponseText;

            
            if (newKey != currentKey)
            {
                try
                {
                    _generalSettingsService.UpdateCondaPath(newKey);
                    MessageBox.Show("Conda path updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Conda path YouTube API Key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void SetYouTubeApiKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        
        string currentKey = _generalSettingsService.GetDecryptedYouTubeApiKey() ?? string.Empty;
        var dialog = new WpfInputDialog("Set YouTube API Key", "Enter your YouTube Data API v3 Key:", currentKey) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            string newKey = dialog.ResponseText;
            
            if (newKey != currentKey)
            {
                try
                {
                    _generalSettingsService.UpdateYouTubeApiKey(newKey); 
                    MessageBox.Show("YouTube API Key updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving YouTube API Key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void SetGitHubApiKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string currentKey = _generalSettingsService.GetDecryptedGitHubApiKey() ?? string.Empty;
        var dialog = new WpfInputDialog("Set GitHub API Key", "Enter your GitHub API Key:", currentKey) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            string newKey = dialog.ResponseText;
            if (newKey != currentKey)
            {
                try
                {
                    _generalSettingsService.UpdateGitHubApiKey(newKey);
                    MessageBox.Show("GitHub API Key updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving GitHub API Key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void SetAzureDevOpsPATMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string currentPAT = _generalSettingsService.GetDecryptedAzureDevOpsPAT() ?? string.Empty;
        var dialog = new WpfInputDialog("Set Azure DevOps PAT", "Enter your Azure DevOps Personal Access Token:", currentPAT) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            string newPAT = dialog.ResponseText;
            if (newPAT != currentPAT)
            {
                try
                {
                    _generalSettingsService.UpdateAzureDevOpsPAT(newPAT);
                    MessageBox.Show("Azure DevOps PAT updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving Azure DevOps PAT: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
    
    private void TestAudioTranscriptionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(async () =>
        {
            string audioExtensions = "*.wav;*.mp3;*.m4a;*.aac;*.ogg;*.oga;*.opus;*.flac;*.wma;*.aiff;*.aif;*.ape;*.ac3;*.dts;*.tta";
            string videoExtensions = "*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.mpg;*.mpeg;*.m2ts;*.ts;*.vob;*.3gp;*.3g2;*.ogv;*.m4v;*.asf";
            string allSupportedExtensions = $"{audioExtensions};{videoExtensions}";

            OpenFileDialog openFileDialog = null;
            bool? dialogResult = null;
            Dispatcher.Invoke(() =>
            {
                openFileDialog = new OpenFileDialog
                {
                    Title = "Select Audio or Video File",
                    Filter = $"All Supported Media|{allSupportedExtensions}|" +
                             $"Audio Files|{audioExtensions}|" +
                             $"Video Files|{videoExtensions}|" +
                             "All Files|*.*",
                    FilterIndex = 1,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    Multiselect = false
                };
                dialogResult = openFileDialog.ShowDialog();
            });

            if (dialogResult != true)
                return;

            var filename = openFileDialog.FileName;
            var condaActivateScriptPath = _generalSettingsService.CurrentSettings.CondaPath;
            string condaPath = Path.Combine(condaActivateScriptPath, "activate.bat");

            if (!File.Exists(condaPath))
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Conda activate script not found in the folder {condaPath}{Environment.NewLine}You can set the path in Edit -> Settings.");
                });
                return;
            }

            var destPath = Path.GetDirectoryName(filename);
            
            if (destPath.EndsWith("/") || destPath.EndsWith("\\"))
                destPath = destPath.Substring(0, destPath.Length - 1);

            string arguments = $"/C {condaPath} && conda activate whisperx_new && whisperx \"{filename}\"  --language en --model  large-v3 --output_dir \"{destPath}\" ";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += (sender2, e2) => Debug.WriteLine(e2.Data);
                process.ErrorDataReceived += (sender2, e2) => Debug.WriteLine(e2.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    var filenameOnly = filename.Split('\\').Last();
                    if (filenameOnly.Contains("."))
                        filenameOnly = filenameOnly.Substring(0, filenameOnly.LastIndexOf('.')) + ".vtt";
                    else
                        filenameOnly += ".json";
                    var fullFilename = Path.Combine(Path.GetDirectoryName(filename)!, filenameOnly);
                    var json = File.ReadAllText(fullFilename);
                    _lastTranscriptionResult = json;
                    Dispatcher.Invoke(() =>
                    {
                        InsertTranscriptionMenuItem.IsEnabled = true;
                        MessageBox.Show(this, "Transcription complete and ready to be inserted.", "Transcription Ready", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                else
                {
                    _lastTranscriptionResult = null;
                    Dispatcher.Invoke(() =>
                    {
                        InsertTranscriptionMenuItem.IsEnabled = false;
                        MessageBox.Show(this, "Unable to transcribe file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
        });
    }

    private void AllowConnectionsOutsideLocalhostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            
            bool currentValue = _generalSettingsService.CurrentSettings.AllowConnectionsOutsideLocalhost;
            _generalSettingsService.CurrentSettings.AllowConnectionsOutsideLocalhost = !currentValue;
            _generalSettingsService.SaveSettings();
            
            
            UpdateAllowConnectionsOutsideLocalhostMenuItem();
            
            
            MessageBox.Show(
                "The server connection setting has been changed. Please restart the application for the changes to take effect.",
                "Restart Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error updating connection setting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void UpdateAllowConnectionsOutsideLocalhostMenuItem()
    {
        
        if (AllowConnectionsOutsideLocalhostMenuItem != null)
        {
            AllowConnectionsOutsideLocalhostMenuItem.IsChecked = _generalSettingsService.CurrentSettings.AllowConnectionsOutsideLocalhost;
        }
    }



    private void LicensesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var licensesWindow = new Dialogs.LicensesWindow(_licensesJsonPath, _nugetLicense1Path, _nugetLicense2Path)
            {
                Owner = this
            };
            licensesWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error displaying licenses: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async Task<bool> ExecutePackingOperationAsync(string outputFilePath)
    {
        // 1. Validate the output path
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            MessageBox.Show("An output file path must be specified.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        // 2. Get project path from settings
        string projectPath = _generalSettingsService.CurrentSettings.ProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            MessageBox.Show("Project path is not set or does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // 3. Show a "working" message
        MessageBox.Show($"Packing project to:\n{outputFilePath}\n\nThis may take a moment.", "Processing", MessageBoxButton.OK, MessageBoxImage.Information);

        try
        {
            // 4. Gather packaging parameters
            var includeExtensions = _generalSettingsService.CurrentSettings.PackerIncludeFileTypes ?? new List<string>();
            var binaryFileExtensions = new List<string>
            {
                ".exe", ".dll", ".pdb", ".obj", ".bin", ".dat", ".zip", ".rar", ".7z", ".tar", ".gz",
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".mp3", ".mp4", ".wav",
                ".avi", ".mov", ".wmv", ".flv", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
            };

            // 5. Call the packager
            string xmlContent = await _projectPackager.CreatePackageAsync(projectPath, includeExtensions, binaryFileExtensions);

            // 6. Write the file
            await File.WriteAllTextAsync(outputFilePath, xmlContent);

            // 7. Show success message
            MessageBox.Show($"Project successfully packed to:\n{outputFilePath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error packing project source code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async void PackProjectSourceCode_Click(object sender, RoutedEventArgs e)
    {
        var saveFileDialog = new SaveFileDialog
        {
            Title = "Save Project Source Code Package",
            Filter = "XML Files|*.xml|All Files|*.*",
            FilterIndex = 1,
            DefaultExt = ".xml",
            FileName = $"{Path.GetFileName(_generalSettingsService.CurrentSettings.ProjectPath)}_SourceCode.xml",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            string selectedPath = saveFileDialog.FileName;

            // Execute the core packing logic
            bool success = await ExecutePackingOperationAsync(selectedPath);

            // If successful, save the path for future repacking
            if (success)
            {
                _generalSettingsService.CurrentSettings.LastPackerOutputFile = selectedPath;
                _generalSettingsService.SaveSettings();
            }
        }
    }

    private async void RepackProjectSourceCode_Click(object sender, RoutedEventArgs e)
    {
        // 1. Get the last saved path from settings
        string lastOutputPath = _generalSettingsService.CurrentSettings.LastPackerOutputFile;
        if (string.IsNullOrWhiteSpace(lastOutputPath))
        {
            MessageBox.Show("Please use 'Pack Project Source Code' first to select an initial output file location.", "Repack Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 2. Execute the core packing logic using the saved path
        await ExecutePackingOperationAsync(lastOutputPath);
    }

    private async void TestReapplyMergeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select Merge Failure JSON File",
            Filter = "JSON Files|*.json|All Files|*.*",
            FilterIndex = 1,
            InitialDirectory = _generalSettingsService.CurrentSettings.ProjectPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (openFileDialog.ShowDialog() == true)
        {
            string mergeFailureJsonPath = openFileDialog.FileName;
            
            try
            {
                
                var app = Application.Current as App;
                var logger = app.Services.GetService(typeof(ILogger<ModifyFileHandler>)) as ILogger;
                var statusMessageService = app.Services.GetService(typeof(IStatusMessageService)) as IStatusMessageService;
                var secondaryAiService = app.Services.GetService(typeof(ISecondaryAiService)) as ISecondaryAiService;
                
                if (logger == null || statusMessageService == null || secondaryAiService == null)
                {
                    MessageBox.Show("Required services not available", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                
                string clientId = Guid.NewGuid().ToString();

                
                var result = await AiStudio4.Core.Tools.CodeDiff.FileOperationHandlers.ModifyFileHandler.ReapplyMergeFailureAsync(
                    mergeFailureJsonPath,
                    logger,
                    statusMessageService,
                    clientId,
                    secondaryAiService);

                
                MessageBox.Show(
                    result.Success ? "Merge successfully reapplied!" : $"Failed to reapply merge: {result.Message}",
                    result.Success ? "Success" : "Error",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reapplying merge: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void SetPackerIncludeTypesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var currentList = _generalSettingsService.CurrentSettings.PackerIncludeFileTypes ?? new System.Collections.Generic.List<string>();
        string defaultValue = string.Join(",", currentList);
        var dialog = new WpfInputDialog(
            "Set Packer Include File Types",
            "Enter comma-separated file extensions to include (e.g., .cs,.xaml,.js):",
            defaultValue)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            var input = dialog.ResponseText;
            var list = input.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s.StartsWith(".") ? s : "." + s)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _generalSettingsService.CurrentSettings.PackerIncludeFileTypes = list;
            _generalSettingsService.SaveSettings();
            MessageBox.Show("Packer include file types updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    
    private void SetPackerExcludeFilenamesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var currentList = _generalSettingsService.CurrentSettings.PackerExcludeFilenames ?? new System.Collections.Generic.List<string>();
        string defaultValue = string.Join(Environment.NewLine, currentList);
        var dialog = new WpfInputDialog(
            "Set Packer Exclude Filenames",
            "Enter filenames to exclude (one per line, '*' is wildcard):",
            defaultValue)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            var input = dialog.ResponseText;
            var list = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _generalSettingsService.CurrentSettings.PackerExcludeFilenames = list;
            _generalSettingsService.SaveSettings();
            MessageBox.Show("Packer exclude filenames updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

private void SetPackerExcludeFolderNamesMenuItem_Click(object sender, RoutedEventArgs e)
{
    var currentList = _generalSettingsService.CurrentSettings.PackerExcludeFolderNames ?? new System.Collections.Generic.List<string>();
    string defaultValue = string.Join(Environment.NewLine, currentList);
    var dialog = new WpfInputDialog(
        "Set Packer Exclude Folder Names",
        "Enter folder names to exclude (one per line, e.g., bin, obj, node_modules):",
        defaultValue)
    {
        Owner = this
    };
    if (dialog.ShowDialog() == true)
    {
        var input = dialog.ResponseText;
        var list = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s) && 
                        !s.Contains(Path.DirectorySeparatorChar) && 
                        !s.Contains(Path.AltDirectorySeparatorChar)) // Ensure only names, no paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _generalSettingsService.CurrentSettings.PackerExcludeFolderNames = list;
        _generalSettingsService.SaveSettings();
        MessageBox.Show("Packer exclude folder names updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

    private void SetConversationZipRetentionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string currentValue = _generalSettingsService.CurrentSettings.ConversationZipRetentionDays.ToString();
        var dialog = new WpfInputDialog(
            "Set Zip Retention",
            "Enter days after which conversations are zipped (e.g., 30). Enter 0 to disable zipping.",
            currentValue)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            if (int.TryParse(dialog.ResponseText, out int days) && days >= 0)
            {
                _generalSettingsService.UpdateConversationZipRetentionDays(days);
                MessageBox.Show($"Conversation zip retention updated to {days} days.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Invalid input. Please enter a non-negative integer.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SetConversationDeleteZippedRetentionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string currentValue = _generalSettingsService.CurrentSettings.ConversationDeleteZippedRetentionDays.ToString();
        var dialog = new WpfInputDialog(
            "Set Zipped Delete Retention",
            "Enter days after which ZIPPED conversations are deleted (e.g., 90). Enter 0 to disable deletion.",
            currentValue)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            if (int.TryParse(dialog.ResponseText, out int days) && days >= 0)
            {
                _generalSettingsService.UpdateConversationDeleteZippedRetentionDays(days);
                MessageBox.Show($"Zipped conversation delete retention updated to {days} days.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Invalid input. Please enter a non-negative integer.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void InsertTranscriptionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastTranscriptionResult))
        {
            await _notificationService.NotifyTranscription(_lastTranscriptionResult);
            MessageBox.Show(this, "Transcription inserted into the prompt.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, "No transcription available to insert.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    
    
    
    
    
    
    private void RebuildMessageRelationships(List<v4BranchedConvMessage> messages)
    {
        if (messages == null || messages.Count <= 1)
            return;

        
        var sortedMessages = messages.OrderBy(m => m.Timestamp).ToList();
        
        
        var systemMessage = sortedMessages.FirstOrDefault(m => m.Role == v4BranchedConvMessageRole.System);
        string currentParentId = systemMessage?.Id;
        
        
        foreach (var message in sortedMessages.Where(m => m.Role != v4BranchedConvMessageRole.System))
        {
            message.ParentId = currentParentId;
            currentParentId = message.Id;
        }
    }

    private async void UploadToGoogleDriveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        UploadToGoogleDriveMenuItem.IsEnabled = false;
        try
        {
            _logger.LogInformation("[UI] Clicked Upload current thread to Google Drive.");

            
            
            
            var allConvs = await _convStorage.GetAllConvs();
            if (allConvs == null || !allConvs.Any())
            {
                MessageBox.Show("No conversations found to upload.", "No Conversations", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            
            
            
            var convToUpload = allConvs.OrderByDescending(c => 
                c.Messages.Any() ? c.Messages.Max(m => m.Timestamp) : DateTime.MinValue
            ).FirstOrDefault();

            if (convToUpload == null)
            {
                 MessageBox.Show("Could not determine a conversation to upload.", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            _logger.LogInformation("Selected conversation '{ConvSummary}' (ID: {ConvId}) for upload.", convToUpload.Summary ?? "Untitled", convToUpload.ConvId);

            
            
            
            string latestMessageId = null;
            if (convToUpload.Messages.Any())
            {
                latestMessageId = convToUpload.Messages.OrderByDescending(m => m.Timestamp).First().Id;
            }
            
            if (latestMessageId == null)
            {
                MessageBox.Show($"Conversation '{convToUpload.Summary ?? convToUpload.ConvId}' has no messages to upload.", "Empty Conversation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            
            string defaultPrimaryModelName = "models/gemini-1.5-pro-latest"; 
            if (!string.IsNullOrEmpty(_generalSettingsService.CurrentSettings.DefaultModelGuid))
            {
                var primaryModel = _generalSettingsService.CurrentSettings.ModelList.FirstOrDefault(
                    m => m.Guid == _generalSettingsService.CurrentSettings.DefaultModelGuid
                );
                if (primaryModel != null)
                {
                    defaultPrimaryModelName = primaryModel.ModelName; 
                }
            }
            
            
            string googleJsonString;
            try
            {
                googleJsonString = AiStudioToGoogleConverter.Convert(convToUpload, latestMessageId, defaultPrimaryModelName);
                _logger.LogInformation("Successfully converted conversation to Google AI Studio format.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting conversation to Google AI Studio format.");
                MessageBox.Show($"Error converting conversation: {ex.Message}", "Conversion Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            
            var safeSummary = SanitizeFileName(convToUpload.Summary ?? "aistudio_conversation");
            var defaultFileName = $"{safeSummary}_{DateTime.Now:yyyyMMddHHmmss}.json";
            
            var fileNameDialog = new WpfInputDialog("Enter Filename", "Enter the filename for Google Drive:", defaultFileName) { Owner = this };
            if (fileNameDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(fileNameDialog.ResponseText))
            {
                MessageBox.Show("Upload cancelled by user.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string fileName = SanitizeFileName(fileNameDialog.ResponseText);
            

            
            _logger.LogInformation("Attempting to upload '{FileName}' to Google Drive folder: Google AI Studio", fileName);
            string fileId = await _googleDriveService.UploadTextFileAsync(fileName, googleJsonString, "Google AI Studio");

            if (!string.IsNullOrEmpty(fileId))
            {
                _logger.LogInformation("Successfully uploaded file to Google Drive. File ID: {FileId}", fileId);
                MessageBox.Show($"Conversation successfully uploaded to Google Drive as '{fileName}'.\nFile ID: {fileId}", "Upload Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                
                MessageBox.Show($"Failed to upload conversation to Google Drive. Please check logs.", "Upload Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (FileNotFoundException fnfEx) 
        {
            _logger.LogError(fnfEx, "Google Drive credentials error during upload.");
            MessageBox.Show(fnfEx.Message, "Google Drive Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred during Google Drive upload process.");
            MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UploadToGoogleDriveMenuItem.IsEnabled = true;
        }
    }

    private string SanitizeFileName(string fileName)
    {
        
        string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
        string sanitized = System.Text.RegularExpressions.Regex.Replace(fileName, invalidRegStr, "_");
        
        return sanitized.Length > 100 ? sanitized.Substring(0, 100) : sanitized;
    }

    private async void AnalyzeDotNetProjects_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Analyze .NET Projects menu item clicked.");
        try
        {
            
            string projectRootPath = _generalSettingsService.CurrentSettings.ProjectPath;
            if (string.IsNullOrEmpty(projectRootPath))
            {
                MessageBox.Show("Project path is not set. Please set a project path first.", "Project Not Ready", MessageBoxButton.OK, MessageBoxImage.Warning);
                _logger.LogWarning("Analysis skipped: Project path not set.");
                return;
            }

            MessageBox.Show("Analysis will begin. This might take a moment.", "Analysis Starting", MessageBoxButton.OK, MessageBoxImage.Information);

            var overallResults = new StringBuilder();
            overallResults.AppendLine("DotNet Project Analysis Results:");
            overallResults.AppendLine($"Analysis Date: {DateTime.Now}");
            overallResults.AppendLine("===================================");

            
            _logger.LogInformation("Analyzing C# files in project: {ProjectPath}", projectRootPath);
            overallResults.AppendLine($"\nProject Directory: {projectRootPath}");
            overallResults.AppendLine("-----------------------------------");
            
            try
            {
                var filesWithMembers = _dotNetProjectAnalyzerService.AnalyzeProjectFiles(projectRootPath);
                
                _logger.LogInformation("Found {Count} files with members", filesWithMembers.Count);
                overallResults.AppendLine($"Found {filesWithMembers.Count} files with analyzable content");
                
                foreach (var fileWithMembers in filesWithMembers)
                {
                    overallResults.AppendLine($"\nFile: {fileWithMembers.FilePath}");
                    overallResults.AppendLine("  Members:");
                    
                    if (fileWithMembers.Members.Any())
                    {
                        var grouped = fileWithMembers.Members.GroupBy(x => x.Namespace);

                        foreach (var group in grouped)
                        {
                            overallResults.AppendLine($"    Namespace: {group.Key}");

                            foreach (var member in group)
                            {
                                overallResults.AppendLine($"        {member.Kind}: {member.Name}");
                            }
                        }
                    }
                    else
                    {
                        overallResults.AppendLine("    (No members found)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing project files: {ErrorMessage}", ex.Message);
                overallResults.AppendLine($"Error analyzing project files: {ex.Message}");
            }
            overallResults.AppendLine("-----------------------------------");

            string outputFileName = "DotNetProjectAnalysis.txt";
            string outputFilePath = System.IO.Path.Combine(projectRootPath, outputFileName);

            
            try
            {
                await System.IO.File.WriteAllTextAsync(outputFilePath, overallResults.ToString());
                _logger.LogInformation("Successfully wrote analysis results to: {OutputFilePath}", outputFilePath);
                MessageBox.Show($"Analysis complete. Results saved to:\n{outputFilePath}", "Analysis Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing analysis results to file: {OutputFilePath}", outputFilePath);
                MessageBox.Show($"Error saving analysis results: {ex.Message}", "File Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred during .NET project analysis.");
            MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateUpdateAvailableMenuItem()
    {
        if (UpdateAvailableMenuItem != null && _updateNotificationService != null)
        {
            if (_updateNotificationService.IsUpdateAvailable)
            {
                UpdateAvailableMenuItem.Header = $"Update Available ({_updateNotificationService.UpdateVersion})";
                UpdateAvailableMenuItem.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateAvailableMenuItem.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateAvailableMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_updateNotificationService != null && _updateNotificationService.IsUpdateAvailable)
            {
                string updateUrl = _updateNotificationService.UpdateUrl;
                if (!string.IsNullOrEmpty(updateUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = updateUrl,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("Update URL is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("No update is currently available.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening update page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EnableWikiSyncMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enableWikiSyncMenuItem = sender as MenuItem;
            bool newValue = enableWikiSyncMenuItem?.IsChecked ?? false;
            _generalSettingsService.CurrentSettings.EnableWikiSystemPromptSync = newValue;
            _generalSettingsService.SaveSettings();
            
            MessageBox.Show(
                "Wiki sync setting has been changed. Changes will take effect on the next application startup.",
                "Setting Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error updating wiki sync setting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfigureWikiSyncMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<ConfigureWikiSyncDialog>>();
                var dialog = new ConfigureWikiSyncDialog(_generalSettingsService, _systemPromptService, logger)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening wiki sync configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateWikiSyncMenuItems()
    {
        var enableWikiSyncMenuItem = this.FindName("EnableWikiSyncMenuItem") as MenuItem;
        if (enableWikiSyncMenuItem != null)
        {
            enableWikiSyncMenuItem.IsChecked = _generalSettingsService.CurrentSettings.EnableWikiSystemPromptSync;
        }
    }

        public string GetDecryptedAzureDevOpsPAT()
        {
            try { return !string.IsNullOrEmpty(_generalSettingsService.CurrentSettings.EncryptedAzureDevOpsPAT) ? GeneralSettingsService.UnprotectData(Convert.FromBase64String(_generalSettingsService.CurrentSettings.EncryptedAzureDevOpsPAT)) : null; }
            catch (CryptographicException) { _generalSettingsService.CurrentSettings.EncryptedAzureDevOpsPAT = null; _generalSettingsService.SaveSettings(); return null; }
        }

    private void SetGoogleCustomSearchApiKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string currentKey = _generalSettingsService.GetDecryptedGoogleCustomSearchApiKey() ?? string.Empty;
        var dialog = new WpfInputDialog("Set Google Custom Search API Key", "Enter your Google Custom Search API Key:", currentKey) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            string newKey = dialog.ResponseText;
            if (newKey != currentKey)
            {
                try
                {
                    _generalSettingsService.UpdateGoogleCustomSearchApiKey(newKey);
                    MessageBox.Show("Google Custom Search API Key updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving Google Custom Search API Key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void ManageProjectsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ManageProjectsDialog(_serviceProvider)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening project management dialog");
            MessageBox.Show($"Error opening project management: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UpdateLlamaCppMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = MessageBox.Show(
                "This will update LlamaCpp to the latest version. The server will be stopped if it's currently running. Continue?",
                "Update LlamaCpp",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Show progress message
                MessageBox.Show(
                    "Updating LlamaCpp... This may take a few minutes. You'll be notified when complete.",
                    "Updating",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Get the LlamaServerService from DI
                var llamaService = _serviceProvider.GetService<ILlamaServerService>();
                if (llamaService == null)
                {
                    MessageBox.Show("LlamaCpp service is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Perform update
                bool success = await llamaService.UpdateLlamaCppAsync();

                if (success)
                {
                    MessageBox.Show(
                        "LlamaCpp has been successfully updated to the latest version.",
                        "Update Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Failed to update LlamaCpp. Please check the application logs for details.",
                        "Update Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating LlamaCpp");
            MessageBox.Show($"Error updating LlamaCpp: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProtectedMcpServerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var mcpServerService = _serviceProvider.GetService<IProtectedMcpServerService>();
            if (mcpServerService == null)
            {
                MessageBox.Show("Protected MCP Server service is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var settingsService = _serviceProvider.GetService<IGeneralSettingsService>();
            var logger = _serviceProvider.GetService<ILogger<ProtectedMcpServerWindow>>();
            var protectedMcpWindow = new ProtectedMcpServerWindow(mcpServerService, settingsService, _serviceProvider, logger)
            {
                Owner = this
            };
            protectedMcpWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening Protected MCP Server window");
            MessageBox.Show($"Error opening Protected MCP Server window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
