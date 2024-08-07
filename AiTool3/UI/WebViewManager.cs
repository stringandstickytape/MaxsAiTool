﻿using Microsoft.Web.WebView2.WinForms;

namespace AiTool3.UI
{
    public class WebViewManager
    {
        public WebView2 webView { get; set; }

        public WebViewManager(WebView2 webViewIn)
        {
            webView = webViewIn;
            webView.Dock = DockStyle.Fill;
            webView.WebMessageReceived += WebView_WebMessageReceived!;
        }

        // event to raise when a menu option is selected
        public event EventHandler<WebNdcContextMenuOptionSelectedEventArgs>? WebNdcContextMenuOptionSelected;
        public event EventHandler<WebNdcNodeClickedEventArgs>? WebNdcNodeClicked;

        private async void WebView_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            string jsonMessage = e.WebMessageAsJson;
            var message = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonMessage);

            switch (message!["type"])
            {
                case "nodeClicked":
                    string clickedNodeId = message["nodeId"];
                    //string clickedNodeLabel = message["nodeLabel"];
                    WebNdcNodeClicked?.Invoke(this, new WebNdcNodeClickedEventArgs(clickedNodeId));
                    // Handle node click (existing code)
                    break;

                case "getContextMenuOptions":
                    var options = new[] { "Save this branch as TXT", "Save this branch as HTML", "Disable" };

                    // Send options back to JavaScript to update the menu
                    string optionsJson = System.Text.Json.JsonSerializer.Serialize(options);
                    string script = $"updateContextMenuOptions({optionsJson});";
                    await webView.ExecuteScriptAsync(script);
                    break;

                case "contextMenuOptionSelected":
                    //string selectedNodeId = message["nodeId"];
                    //string selectedNodeLabel = message["nodeLabel"];
                    //string selectedOption = message["option"];

                    // Handle the selected option
                    //Debug.WriteLine($"Node: {selectedNodeId} ({selectedNodeLabel}), Selected option: {selectedOption}");
                    // raise event
                    //WebNdcContextMenuOptionSelected?.Invoke(this, new WebNdcContextMenuOptionSelectedEventArgs("contex"));
                    break;
                case "editRaw":
                case "saveHtml":
                case "saveTxt":
                    string nodeId = message["nodeId"];
                    WebNdcContextMenuOptionSelected?.Invoke(this, new WebNdcContextMenuOptionSelectedEventArgs(message["type"], nodeId));
                    break;

            }
        }


        public async Task InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async(null);
        }

        public void NavigateToHtml(string html)
        {
            webView.NavigateToString(html);
        }

        public async void CentreOnNode(string guid)
        {
            await webView.CoreWebView2.ExecuteScriptAsync($"centerOnNode('{guid}')");
        }

        public async Task<bool> Clear()
        {
            await webView.CoreWebView2.ExecuteScriptAsync($"clear('')");
            return true;
        }

        public async Task<string> EvaluateJavascriptAsync(string html)
        {
            var a = await webView.CoreWebView2.ExecuteScriptAsync(html);
            return a;
        }

        internal async Task OpenWebViewWithJs(string result, bool showDevTools)
        {
            await webView.EnsureCoreWebView2Async(null);
            await webView.CoreWebView2.Profile.ClearBrowsingDataAsync();

            if (showDevTools) webView.CoreWebView2.OpenDevToolsWindow();

            WebNdcContextMenuOptionSelected += Form_WebNdcContextMenuOptionSelected;
            await InitializeAsync();
            await EvaluateJavascriptAsync(result);
            return;
        }//<insertscripthere/>

        private static void Form_WebNdcContextMenuOptionSelected(object? sender, WebNdcContextMenuOptionSelectedEventArgs e)
        {

        }

        internal async Task OpenWebViewWithHtml(string result)
        {
            WebNdcContextMenuOptionSelected += Form_WebNdcContextMenuOptionSelected;
            await InitializeAsync();
            NavigateToHtml(result);
            return;
        }//<insertscripthere/>

        public async Task CreateNewWebNdc(bool showDevTools, EventHandler<WebNdcContextMenuOptionSelectedEventArgs> webViewNdc_WebNdcContextMenuOptionSelected, EventHandler<WebNdcNodeClickedEventArgs> webViewNdc_WebNdcNodeClicked)
        {
            string js = AssemblyHelper.GetEmbeddedAssembly("AiTool3.JavaScript.NetworkDiagramJavascriptControl.js");
            var css = AssemblyHelper.GetEmbeddedAssembly("AiTool3.JavaScript.NetworkDiagramCssControl.css");


            string html = AssemblyHelper.GetEmbeddedAssembly("AiTool3.JavaScript.NetworkDiagramHtmlControl.html");
            string htmlAndCss = html.Replace("{magiccsstoken}", css);
            string result = htmlAndCss.Replace("<insertscripthere />", js);

            await OpenWebViewWithJs("", showDevTools);

            string d3js = AssemblyHelper.GetEmbeddedAssembly("AiTool3.ThirdPartyJavascript.d3.v7.min.js");

            // how to replace Script tags
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(d3js);
            NavigateToHtml(result);

            WebNdcContextMenuOptionSelected += webViewNdc_WebNdcContextMenuOptionSelected;
            WebNdcNodeClicked += webViewNdc_WebNdcNodeClicked;

        }

        internal void Disable() => webView.Enabled = false;

        internal void Enable() => webView.Enabled = true;
    }

    public class WebNdcContextMenuOptionSelectedEventArgs
    {
        public string MenuOption { get; set; }
        public string Guid { get; set; }

        public WebNdcContextMenuOptionSelectedEventArgs(string menuOption, string guid)
        {
            MenuOption = menuOption;
            Guid = guid;
        }
    }

    public class WebNdcNodeClickedEventArgs
    {
        public string NodeId { get; }

        public WebNdcNodeClickedEventArgs(string nodeId)
        {
            NodeId = nodeId;
        }
    }
}
