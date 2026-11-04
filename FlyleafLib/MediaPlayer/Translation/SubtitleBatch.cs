using System.Collections.Generic;

#nullable enable
namespace FlyleafLib.MediaPlayer.Translation;

public sealed class SubtitleBatchValidationException() : Services.TranslationException(Localization.UiText.Get("Codex returned incomplete or mismatched subtitle IDs."));

public record SubtitleBatchResult(int Id, string Translation, string? Explanation);

public interface ISubtitleBatchTranslator
{
    Task<IReadOnlyList<SubtitleBatchResult>> TranslateBatchAsync(IReadOnlyList<SubtitleData> captions,
        IReadOnlyList<SubtitleData> before, IReadOnlyList<SubtitleData> after, CancellationToken token);
}

public static class SubtitleBatch
{
    // Bound both count and input length; a single unusually long cue still makes progress.
    public static IEnumerable<SubtitleData[]> Split(IEnumerable<SubtitleData> captions, int size, int characterBudget = 6000)
    {
        List<SubtitleData> batch = [];
        int characters = 0;
        foreach (var caption in captions)
        {
            int length = caption.Text?.Length ?? 0;
            if (batch.Count > 0 && (batch.Count >= Math.Clamp(size, 1, 50) || characters + length > characterBudget))
            {
                yield return batch.ToArray();
                batch.Clear();
                characters = 0;
            }
            batch.Add(caption);
            characters += length;
        }
        if (batch.Count > 0) yield return batch.ToArray();
    }
}
