using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Xunit;

namespace Glance.Core.Tests.Rendering;

public class FontProviderTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(700)]
    public void JetBrains_Mono_resolves_to_embedded_face(int weight)
    {
        var tf = new FontProvider().Get(new FontSpec { Family = "JetBrains Mono", Weight = weight });
        Assert.Equal("JetBrains Mono", tf.FamilyName);
        Assert.True(tf.IsFixedPitch);
    }

    [Fact]
    public void JetBrains_Mono_bold_is_a_different_face_than_regular()
    {
        var p = new FontProvider();
        Assert.NotSame(p.Get(new FontSpec { Family = "JetBrains Mono", Weight = 400 }), p.Get(new FontSpec { Family = "JetBrains Mono", Weight = 700 }));
    }
}
