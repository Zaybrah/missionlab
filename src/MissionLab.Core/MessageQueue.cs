using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>What happened to an offered message.</summary>
    public enum MessageTurn
    {
        /// <summary>Show it now, then call <see cref="MessageQueue.Shown"/> if it reached the screen.</summary>
        Now,
        /// <summary>It waits behind earlier ones; <see cref="MessageQueue.Due"/> hands it out later.</summary>
        Waits,
        /// <summary>Eight were already waiting, so it was thrown away.</summary>
        Dropped,
    }

    /// <summary>
    /// The on-screen message queue. A new message pushes the previous one off every screen, so yellow ones wait their
    /// turn and keep a gap between them; red ones are urgent and go at once. Kept free of Unity so the timing can be
    /// tested on a made-up clock.
    /// </summary>
    public sealed class MessageQueue
    {
        /// <summary>Seconds between two messages, long enough to read one.</summary>
        public const float Gap = 4.5f;

        /// <summary>More than this many waiting would leave the players reading old news for half a minute.</summary>
        public const int MaxWaiting = 8;

        private readonly Queue<string> _waiting = new Queue<string>();
        private float _nextAt;

        /// <summary>How many messages are waiting.</summary>
        public int Waiting => _waiting.Count;

        /// <summary>Decides whether a message may be shown now, must wait, or is dropped.</summary>
        public MessageTurn Offer(string text, bool red, float now)
        {
            if (red || (_waiting.Count == 0 && now >= _nextAt)) return MessageTurn.Now;
            if (_waiting.Count >= MaxWaiting) return MessageTurn.Dropped;
            _waiting.Enqueue(text);
            return MessageTurn.Waits;
        }

        /// <summary>
        /// The next waiting message once its turn has come, or null. It leaves the queue even if it then cannot be
        /// shown, so one that fails does not block the rest.
        /// </summary>
        public string Due(float now)
        {
            if (_waiting.Count == 0 || now < _nextAt) return null;
            return _waiting.Dequeue();
        }

        /// <summary>Records that a message reached the screen; the next yellow one waits a full gap from here.</summary>
        public void Shown(float now)
        {
            _nextAt = now + Gap;
        }

        /// <summary>A new mission starts with nothing waiting and no gap to keep.</summary>
        public void Reset()
        {
            _waiting.Clear();
            _nextAt = 0f;
        }
    }
}
