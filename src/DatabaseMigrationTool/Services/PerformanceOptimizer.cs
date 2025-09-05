using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using System.Collections.Concurrent;
using System.Data.Common;

namespace DatabaseMigrationTool.Services
{
    public class PerformanceOptimizer
    {
        public static async Task<List<T>> RunParallel<T>(IEnumerable<Func<Task<T>>> tasks, int maxDegreeOfParallelism = 4)
        {
            var results = new ConcurrentBag<T>();
            var taskSemaphore = new SemaphoreSlim(maxDegreeOfParallelism);
            var runningTasks = new List<Task>();
            
            foreach (var taskFunc in tasks)
            {
                await taskSemaphore.WaitAsync();
                
                runningTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await taskFunc();
                        results.Add(result);
                    }
                    finally
                    {
                        taskSemaphore.Release();
                    }
                }));
            }
            
            await Task.WhenAll(runningTasks);
            return results.ToList();
        }
        
        public static async Task ProcessInParallel<T>(IEnumerable<T> items, Func<T, Task> processAction, int maxDegreeOfParallelism = 4)
        {
            var taskSemaphore = new SemaphoreSlim(maxDegreeOfParallelism);
            var runningTasks = new List<Task>();
            
            foreach (var item in items)
            {
                await taskSemaphore.WaitAsync();
                
                runningTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await processAction(item);
                    }
                    finally
                    {
                        taskSemaphore.Release();
                    }
                }));
            }
            
            await Task.WhenAll(runningTasks);
        }

        public static IAsyncEnumerable<RowData> ProcessDataStreamInParallel(IAsyncEnumerable<RowData> dataStream, Func<RowData, Task<RowData>> transformFunc, int bufferSize = 1000)
        {
            return new ParallelRowDataStream(dataStream, transformFunc, bufferSize);
        }

        private class ParallelRowDataStream : IAsyncEnumerable<RowData>
        {
            private readonly IAsyncEnumerable<RowData> _source;
            private readonly Func<RowData, Task<RowData>> _transformFunc;
            private readonly int _bufferSize;

            public ParallelRowDataStream(
                IAsyncEnumerable<RowData> source, 
                Func<RowData, Task<RowData>> transformFunc, 
                int bufferSize)
            {
                _source = source;
                _transformFunc = transformFunc;
                _bufferSize = bufferSize;
            }

            public IAsyncEnumerator<RowData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new ParallelRowDataStreamEnumerator(_source, _transformFunc, _bufferSize, cancellationToken);
            }

            private class ParallelRowDataStreamEnumerator : IAsyncEnumerator<RowData>
            {
                private readonly IAsyncEnumerable<RowData> _source;
                private readonly Func<RowData, Task<RowData>> _transformFunc;
                private readonly int _bufferSize;
                private readonly CancellationToken _cancellationToken;
                private IAsyncEnumerator<RowData>? _sourceEnumerator;
                private Queue<RowData> _buffer = new();
                private Task? _bufferFillTask;
                private bool _sourceCompleted = false;
                private readonly SemaphoreSlim _bufferSemaphore = new SemaphoreSlim(1, 1);
                
                public RowData Current { get; private set; } = default!;

                public ParallelRowDataStreamEnumerator(
                    IAsyncEnumerable<RowData> source,
                    Func<RowData, Task<RowData>> transformFunc,
                    int bufferSize,
                    CancellationToken cancellationToken)
                {
                    _source = source;
                    _transformFunc = transformFunc;
                    _bufferSize = bufferSize;
                    _cancellationToken = cancellationToken;
                }

                public async ValueTask<bool> MoveNextAsync()
                {
                    if (_sourceEnumerator == null)
                    {
                        _sourceEnumerator = _source.GetAsyncEnumerator(_cancellationToken);
                        _bufferFillTask = FillBufferAsync();
                    }

                    await _bufferSemaphore.WaitAsync(_cancellationToken);
                    try
                    {
                        if (_buffer.Count == 0)
                        {
                            if (_sourceCompleted)
                            {
                                return false;
                            }

                            // Wait for buffer to fill
                            if (_bufferFillTask != null)
                            {
                                await _bufferFillTask;
                                _bufferFillTask = null;
                            }

                            // Check again after waiting
                            if (_buffer.Count == 0)
                            {
                                return false;
                            }
                        }

                        Current = _buffer.Dequeue();

                        // Start filling buffer again if it's getting low and we're not already filling
                        if (_bufferSize > 0 && _buffer.Count < _bufferSize / 2 && !_sourceCompleted && _bufferFillTask == null)
                        {
                            _bufferFillTask = FillBufferAsync();
                        }

                        return true;
                    }
                    finally
                    {
                        _bufferSemaphore.Release();
                    }
                }

                private async Task FillBufferAsync()
                {
                    if (_sourceCompleted || _sourceEnumerator == null)
                    {
                        return;
                    }

                    var tasks = new List<Task<RowData>>();
                    var itemsToProcess = _bufferSize - _buffer.Count;

                    for (int i = 0; i < itemsToProcess; i++)
                    {
                        if (!await _sourceEnumerator.MoveNextAsync())
                        {
                            _sourceCompleted = true;
                            break;
                        }

                        var item = _sourceEnumerator.Current;
                        tasks.Add(_transformFunc(item));
                    }

                    if (tasks.Count > 0)
                    {
                        var results = await Task.WhenAll(tasks);

                        await _bufferSemaphore.WaitAsync(_cancellationToken);
                        try
                        {
                            foreach (var result in results)
                            {
                                _buffer.Enqueue(result);
                            }
                        }
                        finally
                        {
                            _bufferSemaphore.Release();
                        }
                    }
                }

                public async ValueTask DisposeAsync()
                {
                    if (_sourceEnumerator != null)
                    {
                        await _sourceEnumerator.DisposeAsync();
                    }
                }
            }
        }
    }
}