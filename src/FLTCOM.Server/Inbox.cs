using System.Collections.Concurrent;
using System.Threading;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Server
{
    /// <summary>
    /// Messages from clients, already parsed and validated, waiting for the game's main thread.
    /// The server thread posts and one other thread takes.
    /// </summary>
    public sealed class Inbox
    {
        private readonly ConcurrentQueue<Entry> _entries = new ConcurrentQueue<Entry>();

        /// <summary>
        /// Refuses when the client's share would go over its budget. Each client has its own share, so one
        /// that floods is the only one refused, and the whole Inbox holds at most clients times budget.
        /// </summary>
        internal bool TryPost(ClientMessage message, InboxShare share, long cost)
        {
            if (Interlocked.Add(ref share.Used, cost) > share.Budget)
            {
                Interlocked.Add(ref share.Used, -cost);
                return false;
            }
            _entries.Enqueue(new Entry(message, share, cost));
            return true;
        }

        /// <summary>Never blocks. Returns false when nothing is waiting.</summary>
        public bool TryTake(out ClientMessage message)
        {
            if (!_entries.TryDequeue(out Entry entry))
            {
                message = null!;
                return false;
            }
            Interlocked.Add(ref entry.Share.Used, -entry.Cost);
            message = entry.Message;
            return true;
        }

        private readonly struct Entry
        {
            public Entry(ClientMessage message, InboxShare share, long cost)
            {
                Message = message;
                Share = share;
                Cost = cost;
            }

            public ClientMessage Message { get; }
            public InboxShare Share { get; }
            public long Cost { get; }
        }
    }

    /// <summary>One client's part of the Inbox: what its waiting messages cost, against its budget.</summary>
    internal sealed class InboxShare
    {
        public InboxShare(long budget) => Budget = budget;

        public long Budget { get; }

        public long Used;
    }
}
