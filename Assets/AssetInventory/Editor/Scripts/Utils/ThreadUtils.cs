using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    public static class ThreadUtils
    {
        private static SynchronizationContext _mainThreadContext;

        public static void Initialize()
        {
            if (_mainThreadContext == null)
            {
                _mainThreadContext = SynchronizationContext.Current;
                if (_mainThreadContext == null)
                {
                    Debug.LogWarning("SynchronizationContext.Current is null during ThreadUtils.Initialize(). This may cause issues with async operations.");
                }
            }
        }

        public static void InvokeOnMainThread(MethodInfo method, object target, object[] parameters)
        {
            if (_mainThreadContext == null)
            {
                string methodName = method != null ? method.Name : "unknown";
                throw new InvalidOperationException($"ThreadUtils not initialized. Cannot invoke '{methodName}' on main thread. Ensure AI.Init() is called before attempting downloads.");
            }

            if (method == null)
            {
                throw new ArgumentNullException(nameof(method), "Cannot invoke null method on main thread.");
            }

            try
            {
                _mainThreadContext.Post(_ =>
                {
                    try
                    {
                        method.Invoke(target, parameters);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Exception invoking {method.Name} on main thread: {e.Message}\n{e.StackTrace}");
                    }
                }, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to post {method.Name} to main thread: {e.Message}");
                throw;
            }
        }

        public static async Task WithCancellation(this Task task, CancellationToken ct)
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            using (ct.Register(s => ((TaskCompletionSource<object>)s).TrySetResult(null), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                {
                    throw new OperationCanceledException(ct);
                }
            }
            await task.ConfigureAwait(false);
        }

        public static bool IsDisposed(this CancellationTokenSource cts)
        {
            if (cts == null) return true;
            try
            {
                _ = cts.Token;
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }
}