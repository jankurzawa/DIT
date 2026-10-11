namespace Dit.Core;

public sealed record LtcReading(long StartTimeOfDayTicks,int FramesValidated);
// Forward, non-drop 25 fps LTC. BCD field layout and sync word follow the documented LTC bitstream.
// Other rates/directions are refused until qualified; never reinterpret 50 fps field flags as 25 fps.
public static class LtcDecoder
{
    public static LtcReading? Decode(float[] samples,int sampleRate=48000,int fps=25)
    {
        if(fps!=25||sampleRate<16000||samples.Length<sampleRate/5)return null;
        var amplitude=samples.Select(Math.Abs).DefaultIfEmpty().Max();if(amplitude<.01)return null;
        var threshold=amplitude*.15;var sign=samples[0]>0;List<int> edges=[];
        for(var i=1;i<samples.Length;i++)if(sign?samples[i]<-threshold:samples[i]>threshold){edges.Add(i);sign=!sign;}
        var period=sampleRate/(double)(fps*80);List<(bool Bit,int Start)> bits=[];int? halfStart=null;
        for(var i=1;i<edges.Count;i++)
        {
            var length=edges[i]-edges[i-1];
            if(Math.Abs(length-period)<=period*.25){halfStart=null;bits.Add((false,edges[i-1]));}
            else if(Math.Abs(length-period/2)<=period*.2){if(halfStart is {} start){bits.Add((true,start));halfStart=null;}else halfStart=edges[i-1];}
            else{bits.Clear();halfStart=null;}
        }
        List<(long Time,int Start)> frames=[];
        const string sync="0011111111111101";
        for(var end=79;end<bits.Count;end++)
        {
            var start=end-79;var matches=true;for(var b=0;b<16;b++)if(bits[start+64+b].Bit!=(sync[b]=='1')){matches=false;break;}
            if(!matches||bits[start+10].Bit)continue;
            int Value(int offset,int count){var value=0;for(var b=0;b<count;b++)if(bits[start+offset+b].Bit)value|=1<<b;return value;}
            var fu=Value(0,4);var su=Value(16,4);var mu=Value(32,4);var hu=Value(48,4);
            if(fu>9||su>9||mu>9||hu>9)continue;
            var tc=$"{hu+Value(56,2)*10:00}:{mu+Value(40,3)*10:00}:{su+Value(24,3)*10:00}:{fu+Value(8,2)*10:00}";
            if(Timecodes.Parse(tc,fps,1) is {} ticks)frames.Add((ticks,bits[start].Start));
        }
        for(var i=0;i+2<frames.Count;i++)
        {
            var a=frames[i];var b=frames[i+1];var c=frames[i+2];var frameTicks=TimeSpan.TicksPerSecond/fps;
            long Delta(long next,long current)=>(next-current+TimeSpan.TicksPerDay)%TimeSpan.TicksPerDay;
            if(Delta(b.Time,a.Time)!=frameTicks||Delta(c.Time,b.Time)!=frameTicks||Math.Abs((b.Start-a.Start)-sampleRate/(double)fps)>3||Math.Abs((c.Start-b.Start)-sampleRate/(double)fps)>3)continue;
            var origin=(a.Time-(long)a.Start*TimeSpan.TicksPerSecond/sampleRate+TimeSpan.TicksPerDay)%TimeSpan.TicksPerDay;
            return new(origin,3);
        }
        return null;
    }
}
