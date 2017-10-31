using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using DynamicData.Kernel;

namespace DynamicData.Cache.Internal
{
    internal sealed class TransformWithForcedTransform<TDestination, TSource, TKey>
    {
        private readonly IObservable<IChangeSet<TSource, TKey>> _source;
        private readonly Func<TSource, Optional<TSource>, TKey, TDestination> _transformFactory;
        private readonly IObservable<Func<TSource, TKey, bool>> _forceTransform;
        private readonly Action<Error<TSource, TKey>> _exceptionCallback;

        public TransformWithForcedTransform(IObservable<IChangeSet<TSource, TKey>> source, 
            Func<TSource, Optional<TSource>, TKey, TDestination> transformFactory,
            IObservable<Func<TSource, TKey, bool>> forceTransform,
            Action<Error<TSource, TKey>> exceptionCallback = null)
        {
            _source = source;
            _exceptionCallback = exceptionCallback;
            _transformFactory = transformFactory;
            _forceTransform = forceTransform;
        }

        public IObservable<IChangeSet<TDestination, TKey>> Run()
        {
            return Observable.Create<IChangeSet<TDestination, TKey>>(observer =>
            {
                var cache = new ChangeAwareCache<TransformedItemContainer, TKey>();
                var transformer = _source.Select(changes => DoTransform(cache, changes));

                var locker = new object();
                var forced = _forceTransform
                    .Synchronize(locker)
                    .Select(shouldTransform => DoTransform(cache, shouldTransform));

                transformer = transformer.Synchronize(locker).Merge(forced);

                return transformer.NotEmpty().SubscribeSafe(observer);
            });
        }

        private  IChangeSet<TDestination, TKey> DoTransform(ChangeAwareCache<TransformedItemContainer, TKey> cache, Func<TSource, TKey, bool> shouldTransform)
        {
            var toTransform = cache.KeyValues
                .Where(kvp => shouldTransform(kvp.Value.Source, kvp.Key))
                .Select(kvp => new Change<TSource, TKey>(ChangeReason.Update, kvp.Key, kvp.Value.Source, kvp.Value.Source))
                .ToArray();

            var transformed = TransformChanges(toTransform);
            return ProcessUpdates(cache, transformed.ToArray());
        }

        private  IChangeSet<TDestination, TKey> DoTransform(ChangeAwareCache<TransformedItemContainer, TKey> cache, IChangeSet<TSource, TKey> changes)
        {
            var transformed = TransformChanges(changes);
            return ProcessUpdates(cache, transformed.ToArray());
        }

        private TransformResult[] TransformChanges(IEnumerable<Change<TSource, TKey>> changes)
        {
            return changes.Select(Select).AsArray();  
        }
 
        private  TransformResult Select(Change<TSource, TKey> change)
        {
            try
            {
                if (change.Reason == ChangeReason.Add || change.Reason == ChangeReason.Update)
                {
                    var destination = _transformFactory(change.Current, change.Previous, change.Key);
                    return new TransformResult(change, new TransformedItemContainer(change.Current, destination));
                }
                return new TransformResult(change);
            }
            catch (Exception ex)
            {
                //only handle errors if a handler has been specified
                if (_exceptionCallback != null)
                    return new TransformResult(change, ex);
                throw;
            }
        }

        private  IChangeSet<TDestination, TKey> ProcessUpdates(ChangeAwareCache<TransformedItemContainer, TKey> cache, TransformResult[] transformedItems)
        {
            //check for errors and callback if a handler has been specified
            var errors = transformedItems.Where(t => !t.Success).ToArray();
            if (errors.Any())
                errors.ForEach(t => _exceptionCallback(new Error<TSource, TKey>(t.Error, t.Change.Current, t.Change.Key)));

            foreach (var result in transformedItems.Where(t => t.Success))
            {
                TKey key = result.Key;
                switch (result.Change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                        cache.AddOrUpdate(result.Container.Value, key);
                        break;

                    case ChangeReason.Remove:
                        cache.Remove(key);
                        break;

                    case ChangeReason.Refresh:
                        cache.Refresh(key);
                        break;
                }
            }

            var changes = cache.CaptureChanges();
            var transformed = changes.Select(change => new Change<TDestination, TKey>(change.Reason,
                change.Key,
                change.Current.Destination,
                change.Previous.Convert(x => x.Destination),
                change.CurrentIndex,
                change.PreviousIndex));

            return new ChangeSet<TDestination, TKey>(transformed);
        }

        private struct TransformResult
        {
            public Change<TSource, TKey> Change { get; }
            public Exception Error { get; }
            public bool Success { get; }
            public Optional<TransformedItemContainer> Container { get; }
            public TKey Key { get;  }

            public TransformResult(Change<TSource, TKey> change, TransformedItemContainer  container)
                :this()
            {
                Change = change;
                Container = container;
                Success = true;
                Key = change.Key;
            }


            public TransformResult(Change<TSource, TKey> change)
                : this()
            {
                Change = change;
                Container = Optional<TransformedItemContainer>.None;
                Success = true;
                Key = change.Key;
            }

            public TransformResult(Change<TSource, TKey> change, Exception error)
                : this()
            {
                Change = change;
                Error = error;
                Success = false;
                Key = change.Key;
            }
        }

        private sealed class TransformedItemContainer
        {
            public TSource Source { get; }
            public TDestination Destination { get; }

            public TransformedItemContainer(TSource source, TDestination destination)
            {
                Source = source;
                Destination = destination;
            }
        }
    }
}