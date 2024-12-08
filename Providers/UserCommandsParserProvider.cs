using Microsoft.Extensions.Logging;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Providers.Host;
using System.Text.RegularExpressions;

namespace Voxta.SampleProviderApp.Providers;

// This is an example of a provider that receives commands from the chat and forward them to a hardware device.
// In this example we check if there's a command such as [device speed=5] and forward this information to an external device
public class UserCommandsParserProvider(
    IRemoteChatSession session,
    ILogger<UserCommandsParserProvider> logger
)
    : ProviderBase(session, logger)
{
    protected override async Task OnStartAsync()
    {
        await base.OnStartAsync();
        // We only want to handle the user messages
        HandleMessage(ChatMessageRole.User, RemoteChatMessageTiming.Generated, OnUserChatMessage);
    }

    private void OnUserChatMessage(RemoteChatMessage message)
    {
        var patterns = new Dictionary<string, string>
        {
            { @"\b(?:open|start|launch)\b.*\bexplorer\b", "Explorer" },
        };

        // Iterate over patterns to find a match
        foreach (var command in patterns)
        {
            if (Regex.IsMatch(message.Text, command.Key, RegexOptions.IgnoreCase))
            {
                // Handle using switch
                switch (command.Value)
                {
                    case "Explorer":
                        // Example of handling the explorer command
                        OnExplorerOpenCommand();
                        break;
                    default:
                        // If we get here, it means the command matched but was not handled
                        Logger.LogWarning("Unhandled command: {Command}", command);
                        break;
                }
                return;
            }
        }
    }

    // Example command handling methods
    private void OnExplorerOpenCommand()
    {
        Logger.LogInformation("Handling 'Explorer' command.");
        // Add your specific logic here
        updateChat("/note {{ char }} opened the explorer after {{ user }} requested it");
    }

    private void updateChat(string message)
    {
        // Update the AI context so the AI is aware of executed command
        Send(new ClientSendMessage
        {
            SessionId = SessionId,
            Text = message,
        });
    }
}