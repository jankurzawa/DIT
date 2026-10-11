namespace Dit.Core;

public sealed record AudioMatch(int OffsetSamples,double Confidence);
public static class AudioCorrelation
{
    public static AudioMatch? Find(double[] reference,double[] query,int exclusion=5)
    {
        if(query.Length<100||reference.Length<query.Length)return null;
        var mean=query.Average();var q=query.Select(x=>x-mean).ToArray();var energy=q.Sum(x=>x*x);
        if(energy<1e-10)return null;
        var scores=new double[reference.Length-query.Length+1];double best=-1;var offset=0;
        for(var start=0;start<scores.Length;start++)
        {
            double sum=0,square=0,dot=0;
            for(var i=0;i<q.Length;i++){var r=reference[start+i];sum+=r;square+=r*r;dot+=r*q[i];}
            var variance=square-sum*sum/q.Length;
            var score=variance>1e-10?dot/Math.Sqrt(variance*energy):0;scores[start]=score;
            if(score>best){best=score;offset=start;}
        }
        var second=scores.Where((_,i)=>Math.Abs(i-offset)>exclusion).DefaultIfEmpty(0).Max();
        return best>=.92&&best-second>=.05?new(offset,best):null;
    }
    public static double[] Envelope(ReadOnlySpan<float> samples,int block=40)
    {
        var output=new double[samples.Length/block];
        for(var i=0;i<output.Length;i++){double energy=0;for(var j=0;j<block;j++){var s=samples[i*block+j];energy+=s*s;}output[i]=Math.Sqrt(energy/block);}
        return output;
    }
}
