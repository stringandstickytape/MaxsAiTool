﻿using AiTool3;
using AiTool3.ApiManagement;
using AiTool3.Providers;
using Newtonsoft.Json;

internal partial class ModelUsageManager
{
    private Model model;
    public TokenUsage TokensUsed { get; set; }

    public string Filename => $"TokenUsage\\TokenUsage-{model.ToString()}.json";

    public static string GetTokenUsageFilenameFromModel(Model model) => $"TokenUsage\\TokenUsage-{model.ToString()}.json";
    public ModelUsageManager(Model model)
    {
        this.model = model;

        if (File.Exists(Filename))
        {
            var json = File.ReadAllText(Filename);
            TokensUsed = JsonConvert.DeserializeObject<TokenUsage>(json);
        }
        else
        {
            TokensUsed = new TokenUsage("", "");
        }
    }

    internal void AddTokensAndSave(TokenUsage tokenUsage)
    {
        TokensUsed.InputTokens += tokenUsage.InputTokens;
        TokensUsed.OutputTokens += tokenUsage.OutputTokens;

        var json = JsonConvert.SerializeObject(TokensUsed);
        File.WriteAllText(Filename, json);
    }

    public static void ShowUsageStatistics(SettingsSet settings)
    {
        UsageStatisticsForm form = new UsageStatisticsForm(settings);
        form.Show();
        var f2 = new ModelCostPerOutputTokenForm(settings);
        f2.Show();
    }
}