using System;
using System.Collections.Generic;

namespace AtlasAI.Streaming
{
    public sealed record AddonCatalog(string Type, string Id);

    public sealed record AddonManifest(
        string Id,
        string Name,
        IReadOnlyList<string> Resources,
        IReadOnlyList<string> Types,
        IReadOnlyList<AddonCatalog> Catalogs);
}

