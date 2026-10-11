using Dit.Core;
using Xunit;

namespace Dit.Tests;

public sealed class AudioTests
{
    [Fact] public void FindsKnownShiftWithGainAndOffset()
    {
        var random=new Random(42);var reference=Enumerable.Range(0,1000).Select(_=>random.NextDouble()).ToArray();var query=reference.Skip(321).Take(300).Select(x=>x*2+.4).ToArray();var match=AudioCorrelation.Find(reference,query);Assert.NotNull(match);Assert.Equal(321,match.OffsetSamples);Assert.True(match.Confidence>.999);
    }
    [Fact] public void SilenceIsNotSynchronization()=>Assert.Null(AudioCorrelation.Find(new double[500],new double[200]));
    [Fact] public void RepeatedAmbiguousPatternIsNotSynchronization()
    {var signal=Enumerable.Range(0,500).Select(i=>(double)(i%11)).ToArray();Assert.Null(AudioCorrelation.Find(signal,signal.Take(200).ToArray()));}
    [Fact] public void EnvelopePreservesTimeScale(){var samples=Enumerable.Repeat(1f,4000).ToArray();var envelope=AudioCorrelation.Envelope(samples);Assert.Equal(100,envelope.Length);Assert.All(envelope,x=>Assert.Equal(1,x));}
}
