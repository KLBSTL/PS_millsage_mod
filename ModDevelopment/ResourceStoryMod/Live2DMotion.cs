using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ResourceStoryMod.Story
{
    // Cubism motion3 curve evaluation. Existing authored parameter curves only.
    public sealed class Live2DMotion
    {
        public sealed class Curve {public string Id;public double[] Segments;}
        public Curve[] Curves {get;private set;}
        public double Duration {get;private set;}
        public bool Loop {get;private set;}
        public static Live2DMotion Read(string json)
        {
            using var d=JsonDocument.Parse(json);var r=d.RootElement;var meta=r.GetProperty("Meta");
            var duration=meta.GetProperty("Duration").GetDouble();if(!double.IsFinite(duration)||duration<=0)throw new FormatException("Bad motion duration");
            return new Live2DMotion{Duration=duration,Loop=meta.TryGetProperty("Loop",out var loop)&&loop.GetBoolean(),Curves=r.GetProperty("Curves").EnumerateArray().Where(x=>x.GetProperty("Target").GetString()=="Parameter").Select(x=>new Curve{Id=x.GetProperty("Id").GetString(),Segments=x.GetProperty("Segments").EnumerateArray().Select(v=>v.GetDouble()).ToArray()}).ToArray()};
        }
        public static float Evaluate(double[] s,double time)
        {
            if(s==null||s.Length<2)throw new FormatException("Empty curve");double x=s[0],y=s[1];if(time<=x)return (float)y;
            for(int i=2;i<s.Length;){int kind=(int)s[i++];int size=kind==1?6:kind is 0 or 2 or 3?2:throw new FormatException("Unknown curve segment");if(i+size>s.Length)throw new FormatException("Truncated curve");double endX=s[i+size-2],endY=s[i+size-1];
                if(time<=endX){if(endX<=x)return (float)endY;double t=RuntimeCompat.Clamp((time-x)/(endX-x),0,1);
                    if(kind==2)return (float)y;if(kind==3)return (float)endY;
                    if(kind==0)return (float)(y+(endY-y)*t);
                    // Also handles unrestricted time handles: solve the x cubic.
                    double lo=0,hi=1;for(int k=0;k<24;k++){t=(lo+hi)*.5;double q=1-t;double tx=q*q*q*x+3*q*q*t*s[i]+3*q*t*t*s[i+2]+t*t*t*endX;if(tx<time)lo=t;else hi=t;}
                    double a=1-t;return (float)(a*a*a*y+3*a*a*t*s[i+1]+3*a*t*t*s[i+3]+t*t*t*endY);
                }x=endX;y=endY;i+=size;
            }return (float)y;
        }
        public void Apply(float[] values,IReadOnlyDictionary<string,int> ids,double time,bool forceLoop=false)
        {
            time=forceLoop||Loop?time%Duration:Math.Min(time,Duration);foreach(var c in Curves)if(ids.TryGetValue(c.Id,out int i))values[i]=Evaluate(c.Segments,time);
        }
    }
}
