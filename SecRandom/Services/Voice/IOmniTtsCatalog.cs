using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Voice;

/// <summary>
/// Optional OmniTTS capability surface: models are fetched from the selected
/// provider API when available, and users may always type a model manually.
/// The model list is never a fixed hardcoded set.
/// </summary>
public interface IOmniTtsCatalog
{
    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);
}
