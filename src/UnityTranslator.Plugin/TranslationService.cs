using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UnityTranslator
{
    internal enum TextKind
    {
        UguiText,
        TmpText,
        TextMeshText
    }

    internal sealed class PendingEntry
    {
        public readonly object Sync = new object();
        public readonly List<TextTarget> Targets = new List<TextTarget>();
    }

    internal sealed class TextTarget
    {
        private readonly WeakReference<Component> _reference;

        public readonly TextKind Kind;
        public readonly string Original;
        public readonly string ObjectPath;

        public TextTarget(Component component, TextKind kind, string original)
        {
            _reference = new WeakReference<Component>(component);
            Kind = kind;
            Original = original;
            ObjectPath = TextHooks.GetObjectPath(component);
        }

        public Component GetComponent()
        {
            Component component;
            return _reference.TryGetTarget(out component) ? component : null;
        }
    }

    internal sealed class ResultEntry
    {
        public string Source;
        public string Translated;
        public string DetectedLanguage;
        public bool Success;
        public string Error;
    }

    internal sealed class TranslationService
    {
        private const int QueueCapacity = 20000;

        private readonly string _targetLanguage;
        private readonly string _cachePath;
        private readonly ITranslator _translator;
        private readonly int _workerCount;
        private readonly int _delayMs;

        private readonly ConcurrentDictionary<string, string> _cache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, PendingEntry> _pending =
            new ConcurrentDictionary<string, PendingEntry>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<ResultEntry> _results = new ConcurrentQueue<ResultEntry>();
        private readonly BlockingCollection<string> _queue =
            new BlockingCollection<string>(new ConcurrentQueue<string>(), QueueCapacity);
        private readonly ConcurrentDictionary<string, DateTime> _failures =
            new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly HashSet<string> _translatedOutputs = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _outputsSync = new object();
        private readonly List<Thread> _threads = new List<Thread>();

        private volatile bool _stopping;
        private volatile bool _dirty;
        private string _lastDetected;

        public TranslationService(string targetLanguage, string cachePath, ITranslator translator,
                                  int workerCount, int delayMs)
        {
            _targetLanguage = targetLanguage;
            _cachePath = cachePath;
            _translator = translator;
            _workerCount = Math.Max(1, Math.Min(8, workerCount));
            _delayMs = Math.Max(0, delayMs);
        }

        public int CacheCount { get { return _cache.Count; } }
        public string TargetLanguage { get { return _targetLanguage; } }
        public string TranslatorName { get { return _translator.Name; } }

        public void Start()
        {
            LoadCache();
            for (int i = 0; i < _workerCount; i++)
            {
                Thread thread = new Thread(WorkerLoop);
                thread.IsBackground = true;
                thread.Name = "UnityTranslator-" + (i + 1);
                _threads.Add(thread);
                thread.Start();
            }
        }

        private string CacheKey(string source)
        {
            return _targetLanguage + "\u0001" + source;
        }

        public bool TryGetCached(string source, out string translated)
        {
            translated = null;
            if (source == null) return false;
            return _cache.TryGetValue(CacheKey(source), out translated);
        }

        public bool IsTranslatedOutput(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            lock (_outputsSync)
            {
                return _translatedOutputs.Contains(value);
            }
        }

        public void RememberOutput(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (_outputsSync)
            {
                if (_translatedOutputs.Count > 50000) _translatedOutputs.Clear();
                _translatedOutputs.Add(value);
            }
        }

        public void Request(string source, TextTarget target)
        {
            if (_stopping || string.IsNullOrEmpty(source) || target == null) return;

            string cached;
            if (TryGetCached(source, out cached)) return;

            DateTime failedAt;
            if (_failures.TryGetValue(source, out failedAt) &&
                (DateTime.UtcNow - failedAt).TotalSeconds < 120)
                return;

            bool created = false;
            PendingEntry entry = _pending.GetOrAdd(source, delegate
            {
                created = true;
                return new PendingEntry();
            });

            if (created)
            {
                if (!_queue.TryAdd(source))
                {
                    PendingEntry removed;
                    _pending.TryRemove(source, out removed);
                    TranslationPlugin.LogWarning("Translation queue is full; dropping text: " + Preview(source));
                    return;
                }
            }

            Component component = target.GetComponent();
            if (component == null) return;

            lock (entry.Sync)
            {
                for (int i = entry.Targets.Count - 1; i >= 0; i--)
                {
                    TextTarget existing = entry.Targets[i];
                    Component existingComponent = existing.GetComponent();
                    if (existingComponent == null)
                    {
                        entry.Targets.RemoveAt(i);
                        continue;
                    }
                    if (existing.Kind == target.Kind &&
                        ReferenceEquals(existingComponent, component) &&
                        string.Equals(existing.Original, target.Original, StringComparison.Ordinal))
                        return;
                }
                entry.Targets.Add(target);
            }
        }

        private void WorkerLoop()
        {
            while (!_stopping)
            {
                string source;
                try
                {
                    if (!_queue.TryTake(out source, 500)) continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (_stopping) break;

                string cached;
                if (TryGetCached(source, out cached))
                {
                    _results.Enqueue(new ResultEntry
                    {
                        Source = source,
                        Translated = cached,
                        Success = true
                    });
                    continue;
                }

                RateLimiter.Wait(_delayMs);

                TranslationResult result;
                try
                {
                    result = _translator.Translate(source, _targetLanguage);
                }
                catch (Exception ex)
                {
                    result = TranslationResult.Fail(ex.Message);
                }

                if (result.Success)
                {
                    string output = result.Text;
                    if (output == null) output = source;
                    _cache[CacheKey(source)] = output;
                    _dirty = true;

                    if (!string.IsNullOrEmpty(result.DetectedLanguage) &&
                        !string.Equals(result.DetectedLanguage, _lastDetected, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastDetected = result.DetectedLanguage;
                        TranslationPlugin.LogInfo("Detected game/source language: " + result.DetectedLanguage);
                    }

                    _results.Enqueue(new ResultEntry
                    {
                        Source = source,
                        Translated = output,
                        DetectedLanguage = result.DetectedLanguage,
                        Success = true
                    });
                }
                else
                {
                    _failures[source] = DateTime.UtcNow;
                    _results.Enqueue(new ResultEntry
                    {
                        Source = source,
                        Success = false,
                        Error = result.Error
                    });
                }
            }
        }

        /// <summary>Called once per frame on the Unity main thread.</summary>
        public void Pump()
        {
            for (int i = 0; i < 300; i++)
            {
                ResultEntry result;
                if (!_results.TryDequeue(out result)) break;

                PendingEntry entry;
                _pending.TryRemove(result.Source, out entry);

                if (!result.Success)
                {
                    if (!string.IsNullOrEmpty(result.Error))
                        TranslationPlugin.LogWarning("Translation failed for \"" + Preview(result.Source) +
                                                     "\"" + LocationText(entry) + ": " + result.Error);
                    continue;
                }

                string output = result.Translated;
                if (string.IsNullOrEmpty(output) || string.Equals(output, result.Source, StringComparison.Ordinal))
                    continue;

                RememberOutput(output);

                if (entry == null) continue;
                TextTarget[] targets;
                lock (entry.Sync)
                {
                    targets = entry.Targets.ToArray();
                }

                for (int t = 0; t < targets.Length; t++)
                {
                    TextHooks.ApplyNow(targets[t], output);
                }

                if (TranslationPlugin.LogTranslations)
                {
                    TranslationPlugin.LogInfo("[" + result.DetectedLanguage + " -> " + _targetLanguage + "]" +
                                              LocationText(entry) + " " +
                                              Preview(result.Source) + "  =>  " + Preview(output));
                }
            }
        }

        private static string LocationText(PendingEntry entry)
        {
            if (entry == null) return string.Empty;
            TextTarget first = null;
            int count = 0;
            lock (entry.Sync)
            {
                for (int i = 0; i < entry.Targets.Count; i++)
                {
                    TextTarget target = entry.Targets[i];
                    if (string.IsNullOrEmpty(target.ObjectPath)) continue;
                    if (first == null) first = target;
                    count++;
                }
            }
            if (first == null) return string.Empty;
            string suffix = count > 1 ? " (+" + (count - 1) + ")" : string.Empty;
            return " [" + first.ObjectPath + suffix + "]";
        }

        public void SaveCacheIfDirty()
        {
            if (!_dirty) return;
            SaveCache();
        }

        private void SaveCache()
        {
            _dirty = false;
            KeyValuePair<string, string>[] snapshot = _cache.ToArray();
            string directory = Path.GetDirectoryName(_cachePath);
            try
            {
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temp = _cachePath + ".tmp";
                using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("# UnityTranslator cache v1; target=" + _targetLanguage);
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        string key = snapshot[i].Key;
                        int split = key.IndexOf('\u0001');
                        if (split < 0) continue;
                        string source = key.Substring(split + 1);
                        string translation = snapshot[i].Value;
                        writer.Write(Convert.ToBase64String(Encoding.UTF8.GetBytes(source)));
                        writer.Write('\t');
                        writer.Write(Convert.ToBase64String(Encoding.UTF8.GetBytes(translation)));
                        writer.WriteLine();
                    }
                }

                if (File.Exists(_cachePath)) File.Delete(_cachePath);
                File.Move(temp, _cachePath);
            }
            catch (Exception ex)
            {
                _dirty = true;
                TranslationPlugin.LogWarning("Could not save translation cache: " + ex.Message);
            }
        }

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(_cachePath)) return;
                int loaded = 0;
                using (StreamReader reader = new StreamReader(_cachePath, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        int tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        try
                        {
                            string source = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(0, tab)));
                            string translation = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(tab + 1)));
                            if (source.Length == 0) continue;
                            _cache[CacheKey(source)] = translation;
                            loaded++;
                        }
                        catch { }
                    }
                }
                TranslationPlugin.LogInfo("Loaded " + loaded + " cached translation(s) for " + _targetLanguage);
            }
            catch (Exception ex)
            {
                TranslationPlugin.LogWarning("Could not load translation cache: " + ex.Message);
            }
        }

        public void Shutdown()
        {
            if (_stopping) return;
            _stopping = true;
            try { _queue.CompleteAdding(); } catch { }
            SaveCacheIfDirty();
        }

        internal static string Preview(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string value = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (value.Length > 100) value = value.Substring(0, 100) + "...";
            return value;
        }
    }
}
