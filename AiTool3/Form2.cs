﻿using AiTool3.ApiManagement;
using AiTool3.Conversations;
using AiTool3.Settings;
using AiTool3.Topics;
using AiTool3.UI;
using Newtonsoft.Json;
using System.Data;
using System.Diagnostics;
using static AiTool3.UI.NetworkDiagramControl;
using Microsoft.CodeAnalysis;
using AiTool3.Audio;
using AiTool3.Snippets;
using AiTool3.MegaBar.Items;
using Whisper.net.Ggml;
using AiTool3.Providers;
using AiTool3.Helpers;

namespace AiTool3
{
    public partial class Form2 : Form
    {
        public ConversationManager ConversationManager { get; set; } = new ConversationManager();
        public Settings.Settings Settings { get; set; } = AiTool3.Settings.Settings.ReadFromJson();

        public TopicSet TopicSet { get; set; }

        private AudioRecorderManager audioRecorderManager = new AudioRecorderManager(GgmlType.TinyEn);

        public string Base64Image { get; set; }
        public string Base64ImageType { get; set; }

        private AudioRecorder recorder;
        private CancellationTokenSource cts;
        private Task recordingTask;
        private bool isRecording = false;
        public Form2()
        {
            InitializeComponent();
            audioRecorderManager.AudioProcessed += AudioRecorderManager_AudioProcessed;
            ndcConversation.SetContextMenuOptions(new[] { "Save conversation to here as TXT", "Option 2", "Option 3" });
            ndcConversation.MenuOptionSelected += MenuOptionSelected();

            // if topics.json exists, load it
            TopicSet = TopicSet.Load();

            foreach (var topic in TopicSet.Topics)
            {
                cbCategories.Items.Add(topic.Name);
            }

            InitialiseApiList();

            ndcConversation.NodeClicked += NdcConversation_NodeClicked;

            SetSplitContainerEvents();

            rtbInput.KeyDown += (s, e) =>
            {
                CheckForCtrlReturn(e);
            };
            this.KeyDown += (s, e) =>
            {
                CheckForCtrlReturn(e);
            };

            DataGridViewHelper.InitialiseDataGridView(dgvConversations);

            InitialiseMenus();
        }

        private void AudioRecorderManager_AudioProcessed(object? sender, string e)
        {
            if (rtbInput.InvokeRequired)
            {
                rtbInput.Invoke(new Action(() =>
                {
                    rtbInput.Text += e;
                }));
            }
            else
            {
                rtbInput.Text += e;
            }
        }

        private EventHandler<MenuOptionSelectedEventArgs> MenuOptionSelected()
        {
            return (sender, e) =>
            {
                if (e.SelectedOption == "Save conversation to here as TXT")
                {
                    var nodes = ConversationManager.GetParentNodeList();
                    var json = JsonConvert.SerializeObject(nodes);

                    // pretty-print the conversation from the nodes list
                    string conversation = nodes.Aggregate("", (acc, node) => acc + $"{node.Role.ToString()}: {node.Content}" + "\n\n");

                    // get a filename from the user
                    SaveFileDialog saveFileDialog = new SaveFileDialog();
                    saveFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                    saveFileDialog.RestoreDirectory = true;
                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllText(saveFileDialog.FileName, conversation);
                        // open the file in default handler
                        Process.Start(new ProcessStartInfo(saveFileDialog.FileName) { UseShellExecute = true });
                    }
                }
            };
        }

        private void InitialiseMenus()
        {
            // add menu bar with file -> quit
            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.BackColor = Color.Black;
            fileMenu.ForeColor = Color.White;
            var quitMenuItem = new ToolStripMenuItem("Quit");
            quitMenuItem.ForeColor = Color.White;
            quitMenuItem.BackColor = Color.Black;
            quitMenuItem.Click += (s, e) =>
            {
                Application.Exit();
            };

            // add an edit menu
            var editMenu = new ToolStripMenuItem("Edit");
            editMenu.BackColor = Color.Black;
            editMenu.ForeColor = Color.White;
            var clearMenuItem = new ToolStripMenuItem("Clear");
            clearMenuItem.ForeColor = Color.White;
            clearMenuItem.BackColor = Color.Black;
            clearMenuItem.Click += (s, e) =>
            {
                btnClear_Click(null, null);
            };

            // add settings option.  When chosen, invokes SettingsForm modally
            var settingsMenuItem = new ToolStripMenuItem("Settings");
            settingsMenuItem.ForeColor = Color.White;
            settingsMenuItem.BackColor = Color.Black;
            settingsMenuItem.Click += (s, e) =>
            {
                var settingsForm = new SettingsForm(Settings);
                var result = settingsForm.ShowDialog();

                if (result == DialogResult.OK)
                {
                    Settings = settingsForm.NewSettings;
                    AiTool3.Settings.Settings.WriteToJson(Settings);
                }
            };

            fileMenu.DropDownItems.Add(quitMenuItem);
            editMenu.DropDownItems.Add(clearMenuItem);
            editMenu.DropDownItems.Add(settingsMenuItem);
            menuBar.Items.Add(fileMenu);
            menuBar.Items.Add(editMenu);

            // add a specials menu
            var specialsMenu = new ToolStripMenuItem("Specials");
            specialsMenu.BackColor = Color.Black;
            specialsMenu.ForeColor = Color.White;
            var restartMenuItem = new ToolStripMenuItem("Pull Readme and update from latest diff");
            restartMenuItem.ForeColor = Color.White;
            restartMenuItem.BackColor = Color.Black;
            restartMenuItem.Click += (s, e) =>
            {
                AiResponse response, response2;
                SpecialsHelper.GetReadmeResponses((Model)cbEngine.SelectedItem, out response, out response2);
                var snippets = FindSnippets(rtbOutput, $"{response.ResponseText}{Environment.NewLine}{response2.ResponseText}", null, null);

                try
                {
                    var code = snippets.First().Code;
                    // remove first and last lines
                    code = SnipperHelper.StripFirstAndLastLine(code);
                    // get first snippet
                    File.WriteAllText(@"C:\Users\maxhe\source\repos\CloneTest\MaxsAiTool\README.md", code);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error writing to file: {ex.Message}");
                }
            };

            var reviewCodeMenuItem = new ToolStripMenuItem("Review Code");
            reviewCodeMenuItem.ForeColor = Color.White;
            reviewCodeMenuItem.BackColor = Color.Black;
            reviewCodeMenuItem.Click += (s, e) =>
            {
                // go up from the working directory until you get to "MaxsAiTool"
                SpecialsHelper.ReviewCode((Model)cbEngine.SelectedItem, out string userMessage);
                rtbInput.Text = userMessage;

            };
            specialsMenu.DropDownItems.Add(restartMenuItem);
            specialsMenu.DropDownItems.Add(reviewCodeMenuItem);
            menuBar.Items.Add(specialsMenu);

        }


        private void InitialiseApiList()
        {
            foreach (var model in Settings.ApiList.SelectMany(x => x.Models))
            {
                cbEngine.Items.Add(model);
            }

            // preselect the first Local api
            cbEngine.SelectedItem = cbEngine.Items.Cast<Model>().FirstOrDefault(m => m.ServiceName.StartsWith("Local"));
        }

        private void CheckForCtrlReturn(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Return)
            {
                btnGo_Click(null, null);
                e.SuppressKeyPress = true;
            }
        }

        private void SetSplitContainerEvents()
        {
            // for each split container incl in child items
            splitContainer1.Paint += new PaintEventHandler(SplitContainer_Paint);
            splitContainer2.Paint += new PaintEventHandler(SplitContainer_Paint);
            splitContainer3.Paint += new PaintEventHandler(SplitContainer_Paint);
            splitContainer5.Paint += new PaintEventHandler(SplitContainer_Paint);
        }

        private void SplitContainer_Paint(object sender, PaintEventArgs e)
        {
            SplitContainer sc = sender as SplitContainer;

            Rectangle splitterRect = sc.Orientation == Orientation.Horizontal
                ? new Rectangle(0, sc.SplitterDistance, sc.Width, sc.SplitterWidth)
                : new Rectangle(sc.SplitterDistance, 0, sc.SplitterWidth, sc.Height);

            using (SolidBrush brush = new SolidBrush(Color.Gray))
            {
                e.Graphics.FillRectangle(brush, splitterRect);
            }
        }

        private void NdcConversation_NodeClicked(object? sender, NodeClickEventArgs e)
        {

            var clickedCompletion = ConversationManager.CurrentConversation.Messages.FirstOrDefault(c => c.Guid == e.ClickedNode.Guid);
            ConversationManager.PreviousCompletion = clickedCompletion;

            rtbInput.Clear();
            if (ConversationManager.PreviousCompletion.Role == CompletionRole.User)
            {
                rtbInput.Text = ConversationManager.PreviousCompletion.Content;

                ConversationManager.PreviousCompletion = ConversationManager.CurrentConversation.FindByGuid(ConversationManager.PreviousCompletion.Parent);
            }
            if (ConversationManager.PreviousCompletion?.SystemPrompt != null)
            {
                rtbSystemPrompt.Text = ConversationManager.PreviousCompletion.SystemPrompt;
            }
            else rtbSystemPrompt.Text = "";
            FindSnippets(rtbOutput, RtbFunctions.GetFormattedContent(ConversationManager.PreviousCompletion?.Content ?? ""), clickedCompletion.Guid, ConversationManager.CurrentConversation.Messages);
        }

        private SnippetManager snippetManager = new SnippetManager();

        public List<Snippet> FindSnippets(ButtonedRichTextBox richTextBox, string text, string messageGuid, List<CompletionMessage> messages)
        {
            richTextBox.Clear();
            richTextBox.Text = text;
            var snippets = snippetManager.FindSnippets(text);

            // Apply UI formatting
            foreach (var snippet in snippets.Snippets)
            {
                int startIndex = 0;

                // find the end of the line
                var endOfFirstLine = text.IndexOf('\n', snippet.StartIndex);

                // find the length of the first line
                var lengthOfFirstLine = endOfFirstLine - snippet.StartIndex;

                {
                    // var innerCode = snippet.Code.Substring(endOfFirstLine, snippet.Code.Length - endOfFirstLine - 3);
                    richTextBox.Select(endOfFirstLine + 1, snippet.Code.Length - 4 - lengthOfFirstLine);
                    richTextBox.SelectionColor = Color.Yellow;
                    richTextBox.SelectionFont = new Font("Courier New", richTextBox.SelectionFont?.Size ?? 10);

                    var itemsForThisSnippet = MegaBarItemFactory.CreateItems(snippet.Type, snippet.Code, !string.IsNullOrEmpty(snippets.UnterminatedSnippet), messageGuid, messages);
                    richTextBox.AddMegaBar(endOfFirstLine, itemsForThisSnippet.ToArray());
                }
            }

            richTextBox.DeselectAll();

            // scroll to top
            richTextBox.SelectionStart = 0;
            return snippets.Snippets;
        }

        private async void btnGo_Click(object sender, EventArgs e)
        {
            btnGo.Enabled = false;
            var model = (Model)cbEngine.SelectedItem;

            // get the name of the service for the model
            var serviceName = model.ServiceName;

            // instantiate the service from the appropriate api
            var aiService = AiServiceResolver.GetAiService(serviceName);

            Conversation conversation = null;

            conversation = new Conversation();//tbSystemPrompt.Text, tbInput.Text
            conversation.systemprompt = rtbSystemPrompt.Text;
            conversation.messages = new List<ConversationMessage>();
            List<CompletionMessage> nodes = ConversationManager.GetParentNodeList();

            Debug.WriteLine(nodes);

            foreach (var node in nodes)
            {
                if (node.Role == CompletionRole.Root)
                    continue;

                conversation.messages.Add(new ConversationMessage { role = node.Role == CompletionRole.User ? "user" : "assistant", content = node.Content });
            }
            conversation.messages.Add(new ConversationMessage { role = "user", content = rtbInput.Text });

            // fetch the response from the api
            var response = await aiService.FetchResponse(model, conversation, Base64Image, Base64ImageType);

            // work out the cost
            var cost = model.GetCost(response.TokenUsage);

            if (response.SuggestedNextPrompt != null)
            {
                RtbFunctions.GetFormattedContent(response.SuggestedNextPrompt);
            }

            // create a completion message for the user input
            var completionInput = new CompletionMessage
            {
                Role = CompletionRole.User,
                Content = rtbInput.Text,
                Parent = ConversationManager.PreviousCompletion?.Guid,
                Engine = model.ModelName,
                Guid = System.Guid.NewGuid().ToString(),
                Children = new List<string>(),
                SystemPrompt = rtbSystemPrompt.Text,
                InputTokens = response.TokenUsage.InputTokens,
                OutputTokens = 0
            };

            if (response == null)
            {
                MessageBox.Show("Response is null");
                btnGo.Enabled = true;
                return;
            }

            if (ConversationManager.PreviousCompletion != null)
            {
                ConversationManager.PreviousCompletion.Children.Add(completionInput.Guid);
            }

            ConversationManager.CurrentConversation.Messages.Add(completionInput);

            // Create a new completion object to store the response in
            var completionResponse = new CompletionMessage
            {
                Role = CompletionRole.Assistant,
                Content = response.ResponseText,
                Parent = completionInput.Guid,
                Engine = model.ModelName,
                Guid = System.Guid.NewGuid().ToString(),
                Children = new List<string>(),
                SystemPrompt = rtbSystemPrompt.Text,
                InputTokens = 0,
                OutputTokens = response.TokenUsage.OutputTokens
            };

            // add it to the current conversation
            ConversationManager.CurrentConversation.Messages.Add(completionResponse);

            // and display the results in the output box
            FindSnippets(rtbOutput, RtbFunctions.GetFormattedContent(string.Join("\r\n", response.ResponseText)), completionResponse.Guid, ConversationManager.CurrentConversation.Messages);

            if (Settings.NarrateResponses)
            {
                // do this but in a new thread:                 TtsHelper.ReadAloud(rtbOutput.Text);
                var text = rtbOutput.Text;
                Task.Run(() => TtsHelper.ReadAloud(text));
            }

            completionInput.Children.Add(completionResponse.Guid);

            ConversationManager.PreviousCompletion = completionResponse;

            Base64Image = null;
            Base64ImageType = null;

            // draw the network diagram
            DrawNetworkDiagram();

            var currentResponseNode = ndcConversation.GetNodeForGuid(completionResponse.Guid);
            ndcConversation.CenterOnNode(currentResponseNode);
            var summaryModel = Settings.ApiList.First(x => x.ApiName.StartsWith("Ollama")).Models.First();

            string title;
            var row = dgvConversations.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Cells[0]?.Value?.ToString() == ConversationManager.CurrentConversation.ConvGuid);

            // using the title, update the dgvConversations

            ConversationManager.SaveConversation();

            if (row == null)
            {
                dgvConversations.Rows.Insert(0, ConversationManager.CurrentConversation.ConvGuid, ConversationManager.CurrentConversation.Messages[0].Content, ConversationManager.CurrentConversation.Messages[0].Engine, "");

                row = dgvConversations.Rows[0];
            }

            tokenUsageLabel.Text = $"Token Usage: ${cost} : {response.TokenUsage.InputTokens} in --- {response.TokenUsage.OutputTokens} out";

            btnGo.Enabled = true;

            if (row != null && row.Cells[3].Value != null && string.IsNullOrWhiteSpace(row.Cells[3].Value.ToString()))
            {
                row.Cells[3].Value = await ConversationManager.GenerateConversationSummary(summaryModel, Settings.GenerateSummariesUsingLocalAi);
            }
        }

        private void DrawNetworkDiagram()
        {
            // Clear the diagram
            ndcConversation.Clear();

            var root = ConversationManager.CurrentConversation.Messages.FirstOrDefault(c => c.Parent == null);
            if (root == null)
            {
                return;
            }
            var y = 100;

            var rootNode = new Node(root.Content, new Point(300, y), root.Guid, root.InfoLabel);

            // get the model with the same name as the engine
            var model = Settings.ApiList.SelectMany(c => c.Models).Where(x => x.ModelName == root.Engine).FirstOrDefault();

            rootNode.BackColor = root.GetColorForEngine();
            ndcConversation.AddNode(rootNode);

            // recursively draw the children
            DrawChildren(root, rootNode, 300 + 100, ref y);
        }

        private void DrawChildren(CompletionMessage root, Node rootNode, int v, ref int y)
        {
            y += 100;
            foreach (var child in root.Children)
            {
                // get from child string
                var childMsg = ConversationManager.CurrentConversation.Messages.FirstOrDefault(c => c.Guid == child);

                var childNode = new Node(childMsg.Content, new Point(v, y), childMsg.Guid, childMsg.InfoLabel);
                childNode.BackColor = childMsg.GetColorForEngine();
                ndcConversation.AddNode(childNode);
                ndcConversation.AddConnection(rootNode, childNode);
                DrawChildren(childMsg, childNode, v + 100, ref y);
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            rtbInput.Clear();
            rtbSystemPrompt.Clear();
            rtbOutput.Clear();

            ConversationManager.CurrentConversation = new BranchedConversation { ConvGuid = Guid.NewGuid().ToString() };
            ConversationManager.CurrentConversation.AddNewRoot();
            ConversationManager.PreviousCompletion = ConversationManager.CurrentConversation.Messages.First();

            DrawNetworkDiagram();
        }

        private void dgvConversations_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            var clickedGuid = dgvConversations.Rows[e.RowIndex].Cells[0].Value.ToString();

            ConversationManager.LoadConversation(clickedGuid);

            DrawNetworkDiagram();

            ndcConversation.FitAll();
        }

        private void cbCategories_SelectedIndexChanged(object sender, EventArgs e)
        {
            // populate the cbTemplates with the templates for the selected category
            var selected = cbCategories.SelectedItem.ToString();

            var topics = TopicSet.Topics.Where(t => t.Name == selected).ToList();

            var templates = topics.SelectMany(t => t.Templates).Where(x => x.SystemPrompt != null).ToList();

            cbTemplates.Items.Clear();
            cbTemplates.Items.AddRange(templates.Select(t => t.TemplateName).ToArray());
            cbTemplates.DroppedDown = true;
        }

        private void cbTemplates_SelectedIndexChanged(object sender, EventArgs e)
        {
            // find the template
            var template = TopicSet.Topics.First(t => t.Name == cbCategories.SelectedItem.ToString()).Templates.First(t => t.TemplateName == cbTemplates.SelectedItem.ToString());

            btnClear_Click(null, null);

            rtbInput.Clear();
            rtbSystemPrompt.Clear();
            rtbInput.Text = template.InitialPrompt;
            rtbSystemPrompt.Text = template.SystemPrompt;
        }

        private void buttonEditTemplate_Click(object sender, EventArgs e)
        {
            if (cbCategories.SelectedItem == null || cbTemplates.SelectedItem == null) return;

            EditAndSaveTemplate(GetCurrentTemplate());
        }

        private ConversationTemplate GetCurrentTemplate()
        {
            ConversationTemplate template;
            if (cbCategories.SelectedItem == null || cbTemplates.SelectedItem == null)
            {
                return null;
            }
            var category = cbCategories.SelectedItem.ToString();
            var templateName = cbTemplates.SelectedItem.ToString();
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(templateName))
                return null;

            template = TopicSet.Topics.First(t => t.Name == category).Templates.First(t => t.TemplateName == templateName);
            return template;
        }

        private void EditAndSaveTemplate(ConversationTemplate template, bool add = false, string? category = null)
        {
            TemplatesHelper.UpdateTemplates(template, add, category, new Form(), TopicSet, cbCategories, cbTemplates);
        }

        private void buttonAdd_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(cbCategories.Text)) return;

            var template = new ConversationTemplate("System Prompt", "Initial Prompt");

            EditAndSaveTemplate(template, true, cbCategories.Text);
        }

        private void btnRestart_Click(object sender, EventArgs e)
        {
            rtbOutput.Clear();
            ConversationManager.CurrentConversation = new BranchedConversation { ConvGuid = Guid.NewGuid().ToString() };
            ConversationManager.PreviousCompletion = null;
            DrawNetworkDiagram();
        }

        private async void buttonStartRecording_Click(object sender, EventArgs e)
        {
            if (!audioRecorderManager.IsRecording)
            {
                await audioRecorderManager.StartRecording();
                buttonStartRecording.BackColor = Color.Red;
                buttonStartRecording.Text = "Stop\r\nRecord";
            }
            else
            {
                await audioRecorderManager.StopRecording();
                buttonStartRecording.BackColor = Color.Black;
                buttonStartRecording.Text = "Start\r\nRecord";
            }
        }

        private void buttonNewKeepAll_Click(object sender, EventArgs e)
        {
            var lastAssistantMessage = ConversationManager.PreviousCompletion;

            if (lastAssistantMessage.Role == CompletionRole.User)
                lastAssistantMessage = ConversationManager.CurrentConversation.FindByGuid(ConversationManager.PreviousCompletion.Parent);

            var lastUserMessage = ConversationManager.CurrentConversation.FindByGuid(lastAssistantMessage.Parent);

            ConversationManager.CurrentConversation = new BranchedConversation { ConvGuid = Guid.NewGuid().ToString() };

            // create new messages out of the two

            var assistantMessage = new CompletionMessage
            {
                Parent = null,
                Role = CompletionRole.Assistant,
                Content = lastAssistantMessage.Content,
                Engine = lastAssistantMessage.Engine,
                Guid = Guid.NewGuid().ToString(),
                Children = new List<string>()
            };

            var userMessage = new CompletionMessage
            {
                Parent = null,
                Role = CompletionRole.User,
                Content = lastUserMessage.Content,
                Engine = lastUserMessage.Engine,
                Guid = Guid.NewGuid().ToString(),
                Children = new List<string>()
            };

            assistantMessage.Parent = userMessage.Guid;
            userMessage.Children.Add(assistantMessage.Guid);

            ConversationManager.CurrentConversation.Messages.Add(assistantMessage);
            ConversationManager.CurrentConversation.Messages.Add(userMessage);

            ConversationManager.PreviousCompletion = assistantMessage;

            DrawNetworkDiagram();
        }

        private void buttonAttachImage_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = ImageHelpers.ShowAttachImageDialog();

            Base64Image = openFileDialog.FileName != "" ? ImageHelpers.ImageToBase64(openFileDialog.FileName) : "";
            Base64ImageType = openFileDialog.FileName != "" ? ImageHelpers.GetImageType(openFileDialog.FileName) : "";
        }

        private void tbSearch_TextChanged(object sender, EventArgs e)
        {
            foreach (DataGridViewRow row in dgvConversations.Rows)
            {
                if (row.Cells[0].Value == null) continue;
                
                var guid = row.Cells[0].Value.ToString();

                var conv = BranchedConversation.LoadConversation(guid);

                var allMessages = conv.Messages.Select(m => m.Content).ToList();

                row.Visible = allMessages.Any(m => m.Contains(tbSearch.Text, StringComparison.InvariantCultureIgnoreCase));
            }
        }

        private void btnClearSearch_Click(object sender, EventArgs e) => tbSearch.Clear();
    }
}
