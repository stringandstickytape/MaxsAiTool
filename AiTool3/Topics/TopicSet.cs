﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiTool3.Topics
{
    public class TopicSet
    {
        [JsonPropertyName(name: "topics")]
        public List<Topic> Categories { get; set; }


        public TopicSet()
        {
            Categories = new List<Topic>();
        }
        public Topic GetDefaultTopic()
        {
            return Categories.Single(x => x.Guid == "00000000-0000-0000-0000-000000000000");
        }
        internal void Save()
        {
            string jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true
            });

            // Write the JSON to a file
            string fileName = $"Templates\\templates.json";
            string filePath = Path.Combine(Environment.CurrentDirectory, fileName);
            File.WriteAllText(filePath, jsonString);

            return;
        }

        internal static TopicSet Load()
        {
            TopicSet t;

            string jsonText = "";

            if (File.Exists(Path.Combine(Environment.CurrentDirectory, "Templates\\templates.json")))
            {

                jsonText = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Templates\\templates.json"));
            }
            else jsonText = AssemblyHelper.GetEmbeddedAssembly("AiTool3.Defaults.templates.json");

            t = JsonSerializer.Deserialize<TopicSet>(jsonText, new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
                AllowTrailingCommas = true
            });

            if (!File.Exists(Path.Combine(Environment.CurrentDirectory, "Templates\\templates.json")))
                t.Save();

            return t;
        }
    }
}