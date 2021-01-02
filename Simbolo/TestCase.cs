using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Simbolo.InnerNamespace;

public interface IThrow
{
    void Throw();
}

namespace Simbolo
{
    public static class TestCase
    {
        public static void Start()
        {
            var l = long.MaxValue; /* Testing column number */ ProblematicClass.Throw1(new(), 1, ref l, "hi", new []{"1"});
        }
        public static void CallWithGeneric<TThrows>(TThrows throws) where TThrows : IThrow => throws.Throw();
    }

    namespace InnerNamespace
    {
        public static class ProblematicClass
        {
            public static void Throw1(Data data, int i, ref long j, string s, IEnumerable<string> ss) => TestCase.CallWithGeneric(data);
            
        
            public readonly struct Data : IThrow
            {
                public Data(int val)
                {
                    Val = val;
                }

                public int Val { get; }

                public void Throw()
                {
                    var tmpThis = this;
                    var task = Task.Run(() =>
                        throw new InvalidOperationException($"I don't like {tmpThis.Val}"));
                    task.Wait();
                }
            }
        }
    }
}
