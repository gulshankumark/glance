using Glance.Core.Rendering;
using Glance.Core.Tests.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Platform.Windows.Tests;

public class WicJpegCodecTests : JpegCodecContractTests
{
    private static readonly WicJpegCodec Shared = new();

    protected override IJpegCodec Codec => Shared;

    [Fact] public void Factory_picks_wic_when_it_works()
    {
        Assert.IsType<WicJpegCodec>(WicJpegCodec.CreateOrFallback(NullLogger.Instance));
    }
}
