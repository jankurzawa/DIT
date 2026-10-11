using Dit.Core;
using Xunit;

namespace Dit.Tests;

public sealed class LtcTests
{
    private static float[] Signal(bool corrupt=false,bool invert=false)
    {
        List<float> samples=[];var level=.7f;var random=new Random(4);
        for(var frame=0;frame<8;frame++)
        {
            var bits=new bool[80];
            void Set(int start,int count,int value){for(var i=0;i<count;i++)bits[start+i]=((value>>i)&1)!=0;}
            Set(0,4,frame%10);Set(8,2,frame/10);Set(48,4,0);Set(56,2,1);
            var sync="0011111111111101";for(var i=0;i<16;i++)bits[64+i]=sync[i]=='1';
            if(corrupt)bits[64]=!bits[64];
            foreach(var bit in bits){level=-level;for(var sample=0;sample<24;sample++){if(bit&&sample==12)level=-level;samples.Add((invert?-level:level)+(float)(random.NextDouble()-.5)*.04f);}}
        }
        return samples.ToArray();
    }
    [Theory][InlineData(false)][InlineData(true)] public void DecodesForward25FpsWithNoiseAndPolarity(bool invert)
    {var result=LtcDecoder.Decode(Signal(invert:invert));Assert.NotNull(result);Assert.Equal(TimeSpan.FromHours(10).Ticks,result.StartTimeOfDayTicks);Assert.Equal(3,result.FramesValidated);}
    [Fact] public void BrokenSyncIsRejected()=>Assert.Null(LtcDecoder.Decode(Signal(corrupt:true)));
    [Fact] public void UnqualifiedHighRateIsRejected()=>Assert.Null(LtcDecoder.Decode(Signal(),48000,50));
    [Fact] public void SilenceIsRejected()=>Assert.Null(LtcDecoder.Decode(new float[48000]));
}
