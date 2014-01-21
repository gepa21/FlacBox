using System;

#if PARALLEL
using System.Threading;
#endif

namespace FlacBox
{
    abstract class ParallelExecution
    {
        internal abstract void Invoke(params Action[] tasks);
        internal abstract void For(int inclusiveStart, int exclusiveEnd, Action<int> task);
    }

    static class ParallelExecutionFactory
    {
        internal static ParallelExecution CreateParallelExecution(bool useParallelExtensions)
        {
            if (useParallelExtensions)
            {
#if PARALLEL
                return new ParallelExtensions();
#else
                throw new NotSupportedException("Not supported: recompile library with PARALLEL directive");
#endif
            }
            else
            {
                return new NoParallel();
            }
        }

        class NoParallel : ParallelExecution
        {
            internal override void Invoke(params Action[] tasks)
            {
                foreach (Action task in tasks)
                    task.Invoke();
            }

            internal override void For(int inclusiveStart, int exclusiveEnd, Action<int> task)
            {
                for (int i = inclusiveStart; i < exclusiveEnd; i++)
                {
                    task.Invoke(i);
                }
            }
        }

#if PARALLEL
        class ParallelExtensions : ParallelExecution
        {
            internal override void Invoke(params Action[] tasks)
            {
                Thread.MemoryBarrier();
                Parallel.Invoke(tasks);
            }

            internal override void For(int inclusiveStart, int exclusiveEnd, Action<int> task)
            {
                Thread.MemoryBarrier();
                Parallel.For(inclusiveStart, exclusiveEnd, task);
            }
        }
#endif
    }
}
