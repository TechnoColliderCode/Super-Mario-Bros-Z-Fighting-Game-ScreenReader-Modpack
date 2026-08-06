using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SMBZG.Accessibility
{
    /// <summary>Priority levels used to arbitrate between announcement sources.</summary>
    internal static class SpeechPriority
    {
        public const int Remap = 100;
        public const int Error = 90;
        public const int Scene = 50;
        public const int BattleEvent = 45;
        public const int Focus = 40;
        public const int Status = 30;
        public const int Spatial = 30;
        public const int Timer = 20;
    }

    /// <summary>
    /// Central speech hub. Throttles announcements per channel so "everything all the
    /// time" narration does not flood NVDA's speech queue, deduplicates identical text,
    /// and lets important prompts interrupt stale speech.
    /// </summary>
    internal static class Speaker
    {
        private sealed class Pending
        {
            public string Text;
            public string Channel;
            public int Priority;
            public float MinInterval;
        }

        private static readonly Dictionary<string, float> _lastSpokenAt = new Dictionary<string, float>();
        private static readonly List<Pending> _queue = new List<Pending>();
        private static readonly List<string> _recent = new List<string>();

        private static float _lastFlushTime;
        private static float _lastAnyTime;
        private const float MaxSpeaksPerSecond = 6f;
        private const int MaxQueueSize = 12;

        private static Func<bool> _isEnabled;
        private static Action<string> _logSink;

        public static void Configure(Func<bool> isEnabled, Action<string> logSink)
        {
            _isEnabled = isEnabled;
            _logSink = logSink;
        }

        /// <summary>Speaks now if the channel cooldown allows; otherwise queues it.</summary>
        public static void Speak(string text, string channel, int priority, float minInterval = 0f)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            if (_isEnabled != null && !_isEnabled())
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastAnyTime < 0.15f && priority < SpeechPriority.BattleEvent)
            {
                minInterval = Mathf.Max(minInterval, 0.6f);
            }

            float cooldownUntil = GetCooldown(channel);
            if (Time.realtimeSinceStartup >= cooldownUntil)
            {
                TrySpeakNow(text, channel, minInterval);
                return;
            }

            Pending p = new Pending { Text = text, Channel = channel, Priority = priority, MinInterval = minInterval };
            if (!ContainsSameText(p.Text))
            {
                _queue.Add(p);
                if (_queue.Count > MaxQueueSize)
                {
                    _queue.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                    _queue.RemoveAt(0);
                }
            }
        }

        /// <summary>Clears NVDA speech and speaks immediately (used for remap prompts and scene changes).</summary>
        public static void InterruptNow(string text, string channel)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            if (_isEnabled != null && !_isEnabled())
            {
                return;
            }
            NvdaController.StopSpeech();
            _queue.Clear();
            TrySpeakNow(text, channel, 0.25f);
        }

        /// <summary>Flushes queued announcements, respecting the global rate limit. Call every frame.</summary>
        public static void Process()
        {
            if (_queue.Count == 0)
            {
                return;
            }
            _queue.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            float now = Time.realtimeSinceStartup;
            int budget = Mathf.Max(1, Mathf.RoundToInt(MaxSpeaksPerSecond * (now - _lastFlushTime)));
            int count = 0;
            for (int i = _queue.Count - 1; i >= 0 && count < budget; i--)
            {
                Pending p = _queue[i];
                if (now < GetCooldown(p.Channel))
                {
                    continue;
                }
                _queue.RemoveAt(i);
                TrySpeakNow(p.Text, p.Channel, p.MinInterval);
                count++;
            }
            _lastFlushTime = now;
        }

        private static void TrySpeakNow(string text, string channel, float minInterval)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _lastAnyTime + (1f / MaxSpeaksPerSecond) * 0.8f)
            {
                Pending p = new Pending { Text = text, Channel = channel, Priority = SpeechPriority.Status, MinInterval = minInterval };
                if (!ContainsSameText(text))
                {
                    _queue.Add(p);
                }
                return;
            }
            SetCooldown(channel, now + minInterval);
            _lastAnyTime = now;

            AddRecent(text);
            NvdaController.Speak(text);
            if (_logSink != null)
            {
                _logSink(text);
            }
        }

        private static float GetCooldown(string channel)
        {
            float value;
            if (_lastSpokenAt.TryGetValue(channel, out value))
            {
                return value;
            }
            return 0f;
        }

        private static void SetCooldown(string channel, float time)
        {
            _lastSpokenAt[channel] = time;
        }

        private static bool ContainsSameText(string text)
        {
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                if (_recent[i] == text)
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddRecent(string text)
        {
            _recent.Add(text);
            while (_recent.Count > 8)
            {
                _recent.RemoveAt(0);
            }
        }
    }
}
