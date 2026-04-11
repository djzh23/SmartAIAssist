using System.Diagnostics.CodeAnalysis;
using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services.Groq;

/// <summary>
/// Maps Anthropic SDK <see cref="Message"/> history to Groq/OpenAI roles when every turn is plain user/assistant text.
/// Tool-use turns cannot be represented and return false.
/// </summary>
public static class AnthropicMessagesForGroqMapper
{
    public static bool TryMap(IReadOnlyList<Message> history, [NotNullWhen(true)] out List<GroqChatMessage>? mapped)
    {
        mapped = [];
        foreach (var msg in history)
        {
            if (msg.Role is not RoleType.User and not RoleType.Assistant)
            {
                mapped = null;
                return false;
            }

            if (!TryExtractPlainText(msg, out var text))
            {
                mapped = null;
                return false;
            }

            mapped.Add(new GroqChatMessage
            {
                Role = msg.Role == RoleType.User ? "user" : "assistant",
                Content = text,
            });
        }

        return true;
    }

    private static bool TryExtractPlainText(Message msg, [NotNullWhen(true)] out string? text)
    {
        text = null;
        // Message.Content is typed as blocks in the SDK; cast to object so we can match plain string user turns too.
        switch (msg.Content as object)
        {
            case string s:
                text = s;
                return true;
            case IEnumerable<ContentBase> blocks:
            {
                var parts = new List<string>();
                foreach (var block in blocks)
                {
                    switch (block)
                    {
                        case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                            parts.Add(tc.Text);
                            break;
                        default:
                            return false;
                    }
                }

                if (parts.Count == 0)
                    return false;
                text = string.Join("", parts);
                return true;
            }
            default:
                return false;
        }
    }
}
