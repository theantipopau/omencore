using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Centralized startup sequencer that handles boot-time operations with retry logic.
    /// Addresses issues where settings (GPU boost, fan presets, TCC offset) fail to apply
    /// immediately after Windows login due to WMI/BIOS not being ready.
    /// </summary>
    public class StartupSequencer
    {
        private readonly LoggingService _logging;
        private readonly List<StartupTask> _tasks = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        
        public event EventHandler<StartupProgressEventArgs>? ProgressChanged;
        public event EventHandler<StartupCompletedEventArgs>? Completed;
        
        public bool IsRunning { get; private set; }
        public int TotalTasks => _tasks.Count;
        public int CompletedTasks { get; private set; }
        public string? CurrentTaskName { get; private set; }
        
        public StartupSequencer(LoggingService logging)
        {
            _logging = logging;
        }
        
        /// <summary>
        /// Add a startup task to the sequence.
        /// </summary>
        /// <param name="name">Task name for logging and progress display</param>
        /// <param name="action">The async action to execute</param>
        /// <param name="priority">Lower number = higher priority (executed first)</param>
        /// <param name="maxRetries">Maximum retry attempts on failure</param>
        /// <param name="retryDelayMs">Delay between retries in milliseconds</param>
        public void AddTask(string name, Func<CancellationToken, Task<bool>> action, int priority = 100, int maxRetries = 3, int retryDelayMs = 1000)
        {
            _tasks.Add(new StartupTask
            {
                Name = name,
                Action = action,
                Priority = priority,
                MaxRetries = maxRetries,
                RetryDelayMs = retryDelayMs
            });
        }
        
        /// <summary>
        /// Add a simple startup task (void action).
        /// </summary>
        public void AddTask(string name, Action action, int priority = 100, int maxRetries = 3, int retryDelayMs = 1000)
        {
            AddTask(name, async _ =>
            {
                action();
                await Task.CompletedTask;
                return true;
            }, priority, maxRetries, retryDelayMs);
        }
        
        /// <summary>
        /// Execute all startup tasks in priority order with retry logic.
        /// </summary>
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                IsRunning = true;
                CompletedTasks = 0;
                
                // Sort by priority (lower = higher priority)
                _tasks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                
                _logging.Info($"StartupSequencer: Starting {_tasks.Count} tasks...");
                
                var results = new List<StartupTaskResult>();
                
                foreach (var task in _tasks)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    CurrentTaskName = task.Name;
                    ReportProgress(task.Name, CompletedTasks, _tasks.Count);
                    
                    var result = await ExecuteTaskWithRetryAsync(task, cancellationToken);
                    results.Add(result);
                    
                    CompletedTasks++;
                }
                
                var succeeded = results.FindAll(r => r.Success).Count;
                var failed = results.FindAll(r => !r.Success).Count;
                
                _logging.Info($"StartupSequencer: Completed. {succeeded} succeeded, {failed} failed.");
                
                CurrentTaskName = null;
                IsRunning = false;
                
                Completed?.Invoke(this, new StartupCompletedEventArgs
                {
                    Results = results,
                    TotalTasks = _tasks.Count,
                    SuccessCount = succeeded,
                    FailureCount = failed
                });
            }
            finally
            {
                _semaphore.Release();
            }
        }
        
        private async Task<StartupTaskResult> ExecuteTaskWithRetryAsync(StartupTask task, CancellationToken cancellationToken)
        {
            var result = new StartupTaskResult { TaskName = task.Name };
            
            for (int attempt = 1; attempt <= task.MaxRetries; attempt++)
            {
                try
                {
                    result.Attempts = attempt;
                    var success = await task.Action(cancellationToken);
                    
                    if (success)
                    {
                        result.Success = true;
                        if (attempt > 1)
                        {
                            _logging.Info($"✓ {task.Name} succeeded on attempt {attempt}");
                        }
                        else
                        {
                            _logging.Info($"✓ {task.Name} succeeded");
                        }
                        return result;
                    }
                    
                    // Action returned false - treat as failure
                    throw new Exception("Task returned false");
                }
                catch (OperationCanceledException)
                {
                    result.Error = "Cancelled";
                    _logging.Warn($"✗ {task.Name} cancelled");
                    return result;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    
                    if (attempt < task.MaxRetries)
                    {
                        _logging.Warn($"⚠ {task.Name} failed (attempt {attempt}/{task.MaxRetries}): {ex.Message}. Retrying in {task.RetryDelayMs}ms...");
                        await Task.Delay(task.RetryDelayMs, cancellationToken);
                    }
                    else
                    {
                        _logging.Error($"✗ {task.Name} failed after {task.MaxRetries} attempts: {ex.Message}");
                    }
                }
            }
            
            return result;
        }
        
        private void ReportProgress(string taskName, int completed, int total)
        {
            ProgressChanged?.Invoke(this, new StartupProgressEventArgs
            {
                CurrentTask = taskName,
                CompletedTasks = completed,
                TotalTasks = total,
                ProgressPercent = total > 0 ? (int)((completed / (double)total) * 100) : 0
            });
        }
        
        /// <summary>
        /// Clear all tasks (call before re-adding tasks for a new run).
        /// </summary>
        public void ClearTasks()
        {
            _tasks.Clear();
            CompletedTasks = 0;
        }
    }
    
    public class StartupTask
    {
        public string Name { get; set; } = "";
        public Func<CancellationToken, Task<bool>> Action { get; set; } = null!;
        public int Priority { get; set; } = 100;
        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
    }
    
    public class StartupTaskResult
    {
        public string TaskName { get; set; } = "";
        public bool Success { get; set; }
        public int Attempts { get; set; }
        public string? Error { get; set; }
    }
    
    public class StartupProgressEventArgs : EventArgs
    {
        public string CurrentTask { get; set; } = "";
        public int CompletedTasks { get; set; }
        public int TotalTasks { get; set; }
        public int ProgressPercent { get; set; }
    }
    
    public class StartupCompletedEventArgs : EventArgs
    {
        public List<StartupTaskResult> Results { get; set; } = new();
        public int TotalTasks { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
    }
}
