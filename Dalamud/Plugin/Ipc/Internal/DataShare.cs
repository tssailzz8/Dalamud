using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Dalamud.Plugin.Ipc.Exceptions;

namespace Dalamud.Plugin.Ipc.Internal;

/// <summary>
/// This class facilitates sharing data-references of standard types between plugins without using more expensive IPC.
/// </summary>
[ServiceManager.EarlyLoadedService]
internal class DataShare : IServiceType
{
    private readonly Dictionary<string, DataCache> caches = new();

    [ServiceManager.ServiceConstructor]
    private DataShare()
    {
    }

    /// <summary>
    /// If a data cache for <paramref name="tag"/> exists, return the data.
    /// Otherwise, call the function <paramref name="dataGenerator"/> to create data and store it as a new cache.
    /// In either case, the calling assembly will be added to the current consumers on success.
    /// </summary>
    /// <typeparam name="T">The type of the stored data - needs to be a reference type that is shared through Dalamud itself, not loaded by the plugin.</typeparam>
    /// <param name="tag">The name for the data cache.</param>
    /// <param name="dataGenerator">The function that generates the data if it does not already exist.</param>
    /// <returns>Either the existing data for <paramref name="tag"/> or the data generated by <paramref name="dataGenerator"/>.</returns>
    /// <exception cref="DataCacheTypeMismatchError">Thrown if a cache for <paramref name="tag"/> exists, but contains data of a type not assignable to <typeparamref name="T>"/>.</exception>
    /// <exception cref="DataCacheValueNullError">Thrown if the stored data for a cache is null.</exception>
    /// <exception cref="DataCacheCreationError">Thrown if <paramref name="dataGenerator"/> throws an exception or returns null.</exception>
    public T GetOrCreateData<T>(string tag, Func<T> dataGenerator) where T : class
    {
        var callerName = Assembly.GetCallingAssembly().GetName().Name ?? string.Empty;
        if (this.caches.TryGetValue(tag, out var cache))
        {
            if (!cache.Type.IsAssignableTo(typeof(T)))
                throw new DataCacheTypeMismatchError(tag, cache.CreatorAssemblyName, typeof(T), cache.Type);
            cache.UserAssemblyNames.Add(callerName);
            return cache.Data as T ?? throw new DataCacheValueNullError(tag, cache.Type);
        }

        try
        {
            var obj = dataGenerator.Invoke();
            if (obj == null)
                throw new Exception("Returned data was null.");

            cache = new DataCache(callerName, obj, typeof(T));
            this.caches[tag] = cache;
            return obj;
        }
        catch (Exception e)
        {
            throw new DataCacheCreationError(tag, callerName, typeof(T), e);
        }
    }

    /// <summary>
    /// Notifies the DataShare that the calling assembly no longer uses the data stored for <paramref name="tag"/> (or uses it one time fewer).
    /// If no assembly uses the data anymore, the cache will be removed from the data share and if it is an IDisposable, Dispose will be called on it.
    /// </summary>
    /// <param name="tag">The name for the data cache.</param>
    public void RelinquishData(string tag)
    {
        if (!this.caches.TryGetValue(tag, out var cache))
            return;

        var callerName = Assembly.GetCallingAssembly().GetName().Name ?? string.Empty;
        if (!cache.UserAssemblyNames.Remove(callerName) || cache.UserAssemblyNames.Count > 0)
            return;

        this.caches.Remove(tag);
        if (cache.Data is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Obtain the data for the given <paramref name="tag"/>, if it exists and has the correct type.
    /// Add the calling assembly to the current consumers if true is returned.
    /// </summary>
    /// <typeparam name="T">The type of the stored data - needs to be a reference type that is shared through Dalamud itself, not loaded by the plugin.</typeparam>
    /// <param name="tag">The name for the data cache.</param>
    /// <param name="data">The requested data on success, null otherwise.</param>
    /// <returns>True if the requested data exists and is assignable to the requested type.</returns>
    public bool TryGetData<T>(string tag, [NotNullWhen(true)] out T? data) where T : class
    {
        data = null;
        if (!this.caches.TryGetValue(tag, out var cache) || !cache.Type.IsAssignableTo(typeof(T)))
            return false;

        var callerName = Assembly.GetCallingAssembly().GetName().Name ?? string.Empty;
        data = cache.Data as T;
        if (data == null)
            return false;

        cache.UserAssemblyNames.Add(callerName);
        return true;
    }

    /// <summary>
    /// Obtain the data for the given <paramref name="tag"/>, if it exists and has the correct type.
    /// Add the calling assembly to the current consumers if non-null is returned.
    /// </summary>
    /// <typeparam name="T">The type of the stored data - needs to be a reference type that is shared through Dalamud itself, not loaded by the plugin.</typeparam>
    /// <param name="tag">The name for the data cache.</param>
    /// <returns>The requested data.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if <paramref name="tag"/> is not registered.</exception>
    /// <exception cref="DataCacheTypeMismatchError">Thrown if a cache for <paramref name="tag"/> exists, but contains data of a type not assignable to <typeparamref name="T>"/>.</exception>
    /// <exception cref="DataCacheValueNullError">Thrown if the stored data for a cache is null.</exception>
    public T GetData<T>(string tag) where T : class
    {
        if (!this.caches.TryGetValue(tag, out var cache))
            throw new KeyNotFoundException($"The data cache {tag} is not registered.");

        var callerName = Assembly.GetCallingAssembly().GetName().Name ?? string.Empty;
        if (!cache.Type.IsAssignableTo(typeof(T)))
            throw new DataCacheTypeMismatchError(tag, callerName, typeof(T), cache.Type);

        if (cache.Data is not T data)
            throw new DataCacheValueNullError(tag, typeof(T));

        cache.UserAssemblyNames.Add(callerName);
        return data;
    }
}
