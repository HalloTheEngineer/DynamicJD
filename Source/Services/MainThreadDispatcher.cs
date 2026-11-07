using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace DynamicJD.Services
{
    /// <summary>Small main-thread queue for API callbacks. Avoids depending on BSIPA async internals.</summary>
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static MainThreadDispatcher _instance;

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("DynamicJD.Dispatcher");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThreadDispatcher>();
        }

        public static void Enqueue(Action action)
        {
            if (action != null) Queue.Enqueue(action);
        }

        private void Update()
        {
            while (Queue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Warn("Main-thread dispatch failed: " + ex);
                }
            }
        }
    }
}
