﻿using AiTool3.ApiManagement;
using AiTool3.Conversations;
using AiTool3.Interfaces;
using AiTool3.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AiTool3.Providers
{
    internal class Claude : IAiService
    {
        public ToolManager ToolManager { get; set; }
        public bool UseTool { get; set; } = true;

        HttpClient client = new HttpClient();
        bool clientInitialised = false;


        // streaming text received callback event
        public event EventHandler<string> StreamingTextReceived;
        public event EventHandler<string> StreamingComplete;

        public async Task<AiResponse> FetchResponse(Model apiModel, Conversation conversation, string base64image, string base64ImageType, CancellationToken cancellationToken, SettingsSet currentSettings, bool mustNotUseEmbedding, List<string> toolIDs, bool useStreaming = false, bool addEmbeddings = false)
        {
            if (!clientInitialised)
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiModel.Key);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                clientInitialised = true;
            }

            var req = new JObject
            {
                ["model"] = apiModel.ModelName,
                ["system"] = conversation.SystemPromptWithDateTime(),
                ["max_tokens"] = 4096,
                ["stream"] = useStreaming,
                ["temperature"] = currentSettings.Temperature,
            };
            if (toolIDs != null && toolIDs.Any())
            {
                var toolObj = ToolManager.Tools.First(x => x.Name == toolIDs[0]);
                // get first line of toolObj.FullText
                var firstLine = toolObj.FullText.Split("\n")[0];
                firstLine = firstLine.Replace("//", "").Replace(" ", "").Replace("\r", "").Replace("\n", "");

                var colorSchemeTool = AssemblyHelper.GetEmbeddedAssembly($"AiTool3.Tools.{firstLine}");

                colorSchemeTool = Regex.Replace(colorSchemeTool, @"^//.*\n", "", RegexOptions.Multiline);

                var toolx = JObject.Parse(colorSchemeTool);

                req["tools"] = new JArray { toolx };
                req["tool_choice"] = new JObject
                {
                    ["type"] = "tool",
                    ["name"] = toolx["name"].ToString()
                };
            }

            var messagesArray = new JArray();

            for (int i = 0; i < conversation.messages.Count; i++)
            {
                var message = conversation.messages[i];



                var contentArray = new JArray();


                if (message.base64image != null)
                {
                    contentArray.Add(new JObject
                    {
                        ["type"] = "image",
                        ["source"] = new JObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = message.base64type,
                            ["data"] = message.base64image
                        }
                    });
                }

                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = message.content
                });

                var messageObject = new JObject
                {
                    ["role"] = message.role,
                    ["content"] = contentArray
                };

                messagesArray.Add(messageObject);


            }

            req["messages"] = messagesArray;

            if (addEmbeddings)
            {
                var newInput = await OllamaEmbeddingsHelper.AddEmbeddingsToInput(conversation, currentSettings, conversation.messages.Last().content, mustNotUseEmbedding);
                req["messages"].Last["content"].Last["text"] = newInput;
            }


            var json = JsonConvert.SerializeObject(req);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (useStreaming)
            {
                return await HandleStreamingResponse(apiModel, content, cancellationToken);
            }
            else
            {
                return await HandleNonStreamingResponse(apiModel, content, cancellationToken);
            }
        }

        private async Task<AiResponse> HandleStreamingResponse(Model apiModel, StringContent content, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, apiModel.Url) { Content = content };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if(response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var content2 = await response.Content.ReadAsStringAsync();
            }

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var responseBuilder = new StringBuilder();
            var lineBuilder = new StringBuilder();
            var buffer = new byte[48];
            var decoder = Encoding.UTF8.GetDecoder();

            int? inputTokens = null;
            int? outputTokens = null;

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0) break;

                char[] chars = new char[decoder.GetCharCount(buffer, 0, bytesRead)];
                decoder.GetChars(buffer, 0, bytesRead, chars, 0);

                foreach (char c in chars)
                {
                    if (c == '\n')
                    {
                        ProcessLine(lineBuilder.ToString(), responseBuilder, ref inputTokens, ref outputTokens);
                        lineBuilder.Clear();
                    }
                    else
                    {
                        lineBuilder.Append(c);
                    }
                }
            }

            if (lineBuilder.Length > 0)
            {
                ProcessLine(lineBuilder.ToString(), responseBuilder, ref inputTokens, ref outputTokens);
            }

            // call streaming complete
            StreamingComplete?.Invoke(this, null);

            return new AiResponse
            {
                ResponseText = responseBuilder.ToString(),
                Success = true,
                TokenUsage = new TokenUsage(inputTokens?.ToString(), outputTokens?.ToString())
            };
        }

        private void ProcessLine(string line, StringBuilder responseBuilder, ref int? inputTokens, ref int? outputTokens)
        {
            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6);
                if (data == "[DONE]") return;

                try
                {
                    var eventData = JsonConvert.DeserializeObject<JObject>(data);
                    if (eventData["type"].ToString() == "content_block_delta")
                    {
                        var text = eventData["delta"]["text"]?.ToString();
                        if (text == null)
                        {
                            text = eventData["delta"]["partial_json"]?.ToString();
                        }

                        Debug.WriteLine(text);
                        //call streamingtextreceived
                        StreamingTextReceived?.Invoke(this, text);
                        responseBuilder.Append(text);
                    }
                    else if (eventData["type"].ToString() == "message_start")
                    {
                        inputTokens = eventData["message"]["usage"]["input_tokens"].Value<int>();
                    }
                    else if (eventData["type"].ToString() == "message_delta")
                    {
                        outputTokens = eventData["usage"]["output_tokens"].Value<int>();
                    }
                }
                catch (JsonException ex)
                {
                    // Handle JSON parsing error
                    Console.WriteLine($"Error parsing JSON: {ex.Message}");
                }
            }
        }

        private async Task<AiResponse> HandleNonStreamingResponse(Model apiModel, StringContent content, CancellationToken cancellationToken)
        {
            var response = await client.PostAsync(apiModel.Url, content, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            var completion = JsonConvert.DeserializeObject<JObject>(responseString);

            if (completion["type"]?.ToString() == "error")
            {
                return new AiResponse { ResponseText = "error - " + completion["error"]["message"].ToString(), Success = false };
            }
            var inputTokens = completion["usage"]?["input_tokens"]?.ToString();
            var outputTokens = completion["usage"]?["output_tokens"]?.ToString();
            var responseText = "";
            if (completion["content"] != null)
            {
                // is the content type tooL?
                if (completion["content"][0]["type"].ToString() == "tool_use")
                {
                    responseText = completion["content"][0]["input"].First().ToString();
                }
                else responseText = completion["content"][0]["text"].ToString();
            }
            else if (completion["tool_calls"] != null && completion["tool_calls"][0]["function"]["name"].ToString() == "Find-and-replaces")
            {
                responseText = completion["tool_calls"][0]["function"]["arguments"].ToString();
            }

            return new AiResponse { ResponseText = responseText, Success = true, TokenUsage = new TokenUsage(inputTokens, outputTokens) };
        }
    }


    public class CodeSnippet
    {
        public List<float> Embedding { get; set; }
        public string Code { get; set; }

        public string Filename { get; set; }
        public int LineNumber { get; set; }
        public string Namespace { get; set; }
        public string Class { get; set; }
    }



}