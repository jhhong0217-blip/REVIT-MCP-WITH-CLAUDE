using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Concurrent;

namespace RevitMCP.Addin.Server
{
    /// <summary>
    /// 백그라운드 스레드에서 Revit API(메인 스레드)로 작업을 위임합니다.
    /// IExternalEventHandler 패턴을 사용하여 스레드 안전성을 보장합니다.
    /// </summary>
    public static class RevitEventDispatcher
    {
        private static ExternalEvent? _event;
        private static Handler? _handler;

        public static void Initialize(UIApplication app)
        {
            _handler = new Handler();
            _event = ExternalEvent.Create(_handler);
        }

        public static void Dispatch(UIApplication app, Action<Document> action)
        {
            if (_event == null) Initialize(app);
            _handler!.Enqueue(action);
            _event!.Raise();
        }

        // ── 핸들러 ─────────────────────────────────────────────────

        private class Handler : IExternalEventHandler
        {
            private readonly ConcurrentQueue<Action<Document>> _queue = new();

            public void Enqueue(Action<Document> action) => _queue.Enqueue(action);

            public void Execute(UIApplication app)
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                while (_queue.TryDequeue(out var action))
                {
                    try { action(doc); }
                    catch (Exception ex) { Logger.Error($"RevitEventDispatcher.Execute: {ex.Message}"); }
                }
            }

            public string GetName() => "RevitMCP_Dispatcher";
        }
    }
}
