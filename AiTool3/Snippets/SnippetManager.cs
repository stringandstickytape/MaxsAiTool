﻿using System.Text.RegularExpressions;


namespace AiTool3.Snippets
{
    public class SnippetManager
    {

        public SnippetSet FindSnippets(string text)
        {
            string pattern = @"```(.*?)```";
            List<Snippet> snippets = new List<Snippet>();

            var matches = Regex.Matches(text, pattern, RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                if (match.Captures.Count > 0)
                {
                    int startIndex = match.Captures[0].Index;
                    int length = match.Captures[0].Length;

                    string? filename = null;

                    // get the first line
                    var firstLine = text.Substring(startIndex).Split('\n').FirstOrDefault();

                    var type = GetFileExtFromLanguage(firstLine!);

                    var snippetText = text.Substring(startIndex, length);

                    // Remove language name if present at the start of the snippet
                    snippetText = Regex.Replace(snippetText, @"^\s*(\w+)\s*\n", "");

                    snippets.Add(new Snippet
                    {
                        Type = type,
                        Filename = filename!,
                        Content = snippetText.Trim(),
                        StartIndex = startIndex
                    });
                }
            }

            // Check for unterminated three-hashes pairs
            int lastIndex = 0;
            int prevLastIndex = 0;
            bool isOpen = false;
            string? unterminatedSnippet = null;
            while ((lastIndex = text.IndexOf("```", lastIndex)) != -1)
            {
                isOpen = !isOpen;
                lastIndex += 3;
                prevLastIndex = lastIndex;
            }

            if (isOpen)
            {
                // get contents of unterminated pair
                unterminatedSnippet = text.Substring(prevLastIndex);
            }


            return new SnippetSet { Snippets = snippets, UnterminatedSnippet = unterminatedSnippet };
        }

        internal static string GetFileExtFromFirstLine(string firstLine)
        {
            string pattern2 = @"```[a-zA-Z]+";

            string fileExt = ".txt";

            foreach (Match match2 in Regex.Matches(firstLine, pattern2))
            {
                var language = match2.Value.Substring(3);

                fileExt = GetFileExtFromLanguage(language);
            }

            return fileExt;
        }

        internal static string GetFileExtFromLanguage(string language)
        {
            string fileExt;
            if (language == "csharp")
            {
                fileExt = ".cs";
            }
            else if (language == "html" || language == "htm")
            {
                fileExt = ".html";
            }
            else if (language == "txt")
            {
                fileExt = ".txt";
            }
            else if (language == "xml")
            {
                fileExt = ".xml";
            }
            else if (language == "javascript")
            {
                fileExt = ".js";
            }
            else if (language == "python")
            {
                fileExt = ".py";
            }
            else if (language == "json")
            {
                fileExt = ".json";
            }
            else fileExt = $".{language}";
            return fileExt;
        }

        public string ApplySnippetFormatting(string text)
        {
            string pattern = @"```(.*?)```";
            return Regex.Replace(text, pattern, match =>
            {
                if (match.Groups.Count > 1)
                {
                    string snippetText = match.Groups[1].Value;
                    // Remove language name if present at the start of the snippet
                    snippetText = Regex.Replace(snippetText, @"^\s*(\w+)\s*\n", "");
                    return $"<snippet>{snippetText}</snippet>";
                }
                return match.Value;
            }, RegexOptions.Singleline);
        }
    }


    public class SnippetSet
    {
        public List<Snippet> Snippets;

        public string? UnterminatedSnippet { get; set; }
    }
}
