using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Ritgard.Mining;

public record Topic(
    int Id,
    ImmutableDictionary<string, ImmutableArray<(string representation, double score)>> Representations
)
{
    public string GetPreferredTitle()
    {
        if (Representations.TryGetValue("LLM", out var llm))
        {
            return llm.First().representation;
        }
        else if (Representations.TryGetValue("KeyBERT", out var keybert))
        {
            return string.Join(" ", keybert.Select(i => i.representation));
        }
        else if (Representations.TryGetValue("Main", out var main))
        {
            return string.Join(" ", main.Select(i => i.representation));
        }

        return string.Empty;
    }
}
