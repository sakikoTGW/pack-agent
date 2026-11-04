using System;
using System.Collections.Generic;

namespace Pad.Core;

/// <summary>
/// 极小的服务容器（对应 DSH 的 cordis <c>Context</c>）：模块 Provide 接口，消费者 Require；
/// 所有 effect 在 Dispose 时逆序回收。不做插件运行时、不做多级 scope——PAD 用不到。
/// </summary>
public sealed class PadContext : IDisposable
{
    readonly Dictionary<Type, object> _services = [];
    readonly List<IDisposable> _effects = [];
    bool _disposed;

    public void Provide<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_services.ContainsKey(typeof(T)))
            throw new InvalidOperationException($"service already provided: {typeof(T).Name}");
        _services[typeof(T)] = service;
    }

    public T Require<T>() where T : class =>
        _services.TryGetValue(typeof(T), out var service)
            ? (T)service
            : throw new InvalidOperationException($"service not available: {typeof(T).Name}");

    public bool Has<T>() where T : class => _services.ContainsKey(typeof(T));

    public IDisposable Effect(Action dispose) => Effect(new CallbackDisposable(dispose));

    public IDisposable Effect(IDisposable effect)
    {
        _effects.Add(effect);
        return effect;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            try { _effects[i].Dispose(); }
            catch { /* a broken disposer must not hide the others */ }
        }
        _effects.Clear();
        _services.Clear();
    }

    sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            dispose();
        }
    }
}
