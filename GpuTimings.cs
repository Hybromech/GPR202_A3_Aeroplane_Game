using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
namespace GPR202_Assessment3
{
    // Four queries per pass; skip sampling rather than wait for a busy GPU.
    public sealed class GpuTimings : IDisposable
    {
        private sealed class Pass
        {
            public readonly int[] Queries = new int[4];
            public readonly bool[] Pending = new bool[4];
            public double Sum;
            public int Samples;
            public Pass() { for (int i = 0; i < 4; i++) Queries[i] = GL.GenQuery(); }
        }
        private readonly Dictionary<string, Pass> passes = new();
        private readonly Stopwatch interval = Stopwatch.StartNew();
        private Pass? active;
        private int slot;
        public void Begin(string name, bool enabled)
        {
            if (active != null) throw new InvalidOperationException("GPU timers cannot nest.");
            if (!enabled) return;
            if (!passes.TryGetValue(name, out var pass)) { pass = new Pass(); passes.Add(name, pass); }
            for (int i = 0; i < 4; i++)
            {
                if (pass.Pending[i]) continue;
                active = pass; slot = i;
                GL.BeginQuery(QueryTarget.TimeElapsed, pass.Queries[i]);
                return;
            }
        }
        public void End()
        {
            if (active == null) return;
            GL.EndQuery(QueryTarget.TimeElapsed);
            active.Pending[slot] = true;
            active = null;
        }
        public void Collect(bool enabled)
        {
            foreach (var pass in passes.Values)
                for (int i = 0; i < 4; i++)
                {
                    if (!pass.Pending[i]) continue;
                    GL.GetQueryObject(pass.Queries[i], GetQueryObjectParam.QueryResultAvailable, out int ready);
                    if (ready == 0) continue;
                    GL.GetQueryObject(pass.Queries[i], GetQueryObjectParam.QueryResult, out long nanoseconds);
                    pass.Pending[i] = false;
                    pass.Sum += nanoseconds / 1000000.0;
                    pass.Samples++;
                }
            if (interval.Elapsed.TotalSeconds < 1) return;
            if (enabled)
                Console.WriteLine("GPU ms (available samples): " + string.Join(" | ", passes
                    .Where(x => x.Value.Samples > 0)
                    .Select(x => $"{x.Key}: {x.Value.Sum / x.Value.Samples:F3}")));
            foreach (var pass in passes.Values) { pass.Sum = 0; pass.Samples = 0; }
            interval.Restart();
        }
        public void Dispose()
        {
            End();
            foreach (var pass in passes.Values) foreach (int query in pass.Queries) GL.DeleteQuery(query);
            passes.Clear();
        }
    }
}
