using System.Text;

using AIStudio.Agents;
using AIStudio.Chat;
using AIStudio.Provider;
using AIStudio.Settings;

namespace AIStudio.Tools.RAG.AugmentationProcesses;

public sealed class AugmentationOne : IAugmentationProcess
{
    #region Implementation of IAugmentationProcess

    /// <inheritdoc />
    public string TechnicalName => "AugmentationOne";
    
    /// <inheritdoc />
    public string UIName => "Standard augmentation process";
    
    /// <inheritdoc />
    public string Description => "This is the standard augmentation process, which uses all retrieval contexts to augment the chat thread.";
    
    /// <inheritdoc />
    public async Task<ChatThread> ProcessAsync(IProvider provider, IContent lastPrompt, ChatThread chatThread, IReadOnlyList<IRetrievalContext> retrievalContexts, CancellationToken token = default)
    {
        var logger = Program.SERVICE_PROVIDER.GetService<ILogger<AugmentationOne>>()!;
        var settings = Program.SERVICE_PROVIDER.GetService<SettingsManager>()!;
        
        if(retrievalContexts.Count == 0)
        {
            logger.LogWarning("No retrieval contexts were issued. Skipping the augmentation process.");
            return chatThread;
        }

        var numTotalRetrievalContexts = retrievalContexts.Count;
        
        // Want the user to validate all retrieval contexts?
        if (settings.ConfigurationData.AgentRetrievalContextValidation.EnableRetrievalContextValidation)
        {
            // Let's get the validation agent & set up its provider:
            var validationAgent = Program.SERVICE_PROVIDER.GetService<AgentRetrievalContextValidation>()!;
            validationAgent.SetLLMProvider(provider);
            
            // Let's validate all retrieval contexts:
            var validationResults = await validationAgent.ValidateRetrievalContextsAsync(lastPrompt, chatThread, retrievalContexts, token);
         
            //
            // Now, filter the retrieval contexts to the most relevant ones:
            //
            var targetWindow = validationResults.DetermineTargetWindow(TargetWindowStrategy.TOP10_BETTER_THAN_GUESSING);
            var threshold = validationResults.GetConfidenceThreshold(targetWindow);
            
            // Filter the retrieval contexts:
            retrievalContexts = validationResults.Where(x => x.RetrievalContext is not null && x.Confidence >= threshold).Select(x => x.RetrievalContext!).ToList();
        }
        
        logger.LogInformation($"Starting the augmentation process over {numTotalRetrievalContexts:###,###,###,###} retrieval contexts.");
        
        //
        // We build a huge prompt from all retrieval contexts:
        //
        var sb = new StringBuilder();
        sb.AppendLine("The following useful information will help you in processing the user prompt:");
        sb.AppendLine();
        
        // Let's convert all retrieval contexts to Markdown:
        await retrievalContexts.AsMarkdown(sb, token);
        
        //
        // Append the entire augmentation to the chat thread,
        // just before the user prompt:
        //
        chatThread.Blocks.Insert(chatThread.Blocks.Count - 1, new()
        {
            Role = ChatRole.RAG,
            Time = DateTimeOffset.UtcNow,
            ContentType = ContentType.TEXT,
            HideFromUser = true,
            Content = new ContentText
            {
                Text = sb.ToString(),
            }
        });
        
        return chatThread;
    }

    #endregion
}