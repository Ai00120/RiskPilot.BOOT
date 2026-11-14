using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The stable identity of the diagnostic menu item. It is declared once here so the host
    /// adapter and its tests can never disagree on the id.
    /// </summary>
    public static class DiagnosticMenu
    {
        public const string ItemId = "RiskPilotDiagnosticsNt8CenterMenuItem";
    }

    /// <summary>
    /// Identifies the NinjaTrader window a menu surface belongs to. Only <see cref="ControlCenter"/>
    /// is a supported owner; every other window is deliberately excluded, so the menu item is never
    /// added to a window that was not designed to carry it.
    /// </summary>
    public enum MenuSurfaceKind
    {
        Unknown = 0,
        ControlCenter = 1,
        Other = 2
    }

    /// <summary>
    /// Decides whether a NinjaTrader window is a supported owner of the diagnostic menu item.
    /// Only the Control Center qualifies; every other window (an Order, Chart, SuperDOM or any
    /// other NinjaTrader window) is deliberately excluded, so the menu item is never added to a
    /// window that was not designed to carry it. It is host-free and unit tested; the host adapter
    /// feeds it the window's type hierarchy as a list of type names.
    /// </summary>
    public static class MenuOwnerClassifier
    {
        public const string ControlCenterTypeName = "ControlCenter";
        public const string ControlCenterTypeFullName = "NinjaTrader.Gui.ControlCenter";

        /// <summary>
        /// Classifies a window from its type hierarchy, supplied as type names (the host adapter
        /// adds both the full and the short name of each type, leaf first). Returns
        /// <see cref="MenuSurfaceKind.ControlCenter"/> when any name is the Control Center, and
        /// <see cref="MenuSurfaceKind.Other"/> otherwise (never a guess).
        /// </summary>
        public static MenuSurfaceKind Classify(IEnumerable<string> typeNames)
        {
            if (typeNames == null)
            {
                return MenuSurfaceKind.Unknown;
            }

            foreach (var name in typeNames)
            {
                if (string.Equals(name, ControlCenterTypeFullName, StringComparison.Ordinal)
                    || string.Equals(name, ControlCenterTypeName, StringComparison.Ordinal))
                {
                    return MenuSurfaceKind.ControlCenter;
                }
            }

            return MenuSurfaceKind.Other;
        }
    }

    /// <summary>Outcome of a registration attempt. Explains why a registration was refused.</summary>
    public enum MenuAttachStatus
    {
        Attached = 0,
        AlreadyAttached = 1,
        SurfaceAlreadyOwned = 2,
        UnsupportedOwner = 3,
        OwnerNotAlive = 4,
        SurfaceUnavailable = 5,
        AddFailed = 6
    }

    /// <summary>
    /// Host-free abstraction over one menu a diagnostic menu item is added to. The host adapter
    /// implements it over a WPF <c>System.Windows.Controls.Menu</c>; a test implements it in memory.
    /// It is strictly additive (add / remove / contains) and can never submit, change or cancel an
    /// order and never touches an account, connection or position.
    /// </summary>
    public interface IMenuSurface
    {
        /// <summary>Number of items the surface currently holds.</summary>
        int ItemCount { get; }

        /// <summary>True when an item with the given id is present.</summary>
        bool Contains(string itemId);

        /// <summary>
        /// Adds an item that runs <paramref name="onClick"/> when activated. Returns false when the
        /// item could not be added (unavailable surface, or an item with that id already exists);
        /// a false result must never be treated as a successful registration.
        /// </summary>
        bool Add(string itemId, Action onClick);

        /// <summary>
        /// Removes every item with the given id and detaches its click handler so no callback (and
        /// therefore no owning window/manager) is retained. Returns true when something was removed.
        /// </summary>
        bool Remove(string itemId);
    }

    /// <summary>
    /// The outcome of a registration attempt: whether the item is owned by the requesting window
    /// after the call, plus the <see cref="MenuAttachStatus"/> explaining the result.
    /// </summary>
    public readonly struct MenuAttachResult
    {
        public MenuAttachResult(bool attached, MenuAttachStatus status)
        {
            Attached = attached;
            Status = status;
        }

        /// <summary>True when the requesting window owns the item after the call.</summary>
        public bool Attached { get; }

        /// <summary>Why the registration succeeded or was refused.</summary>
        public MenuAttachStatus Status { get; }
    }

    /// <summary>
    /// Tracks exactly which windows own the diagnostic menu item and on which menu each item was
    /// placed. It replaces the previous global <c>bool _added</c> flag, which could not tell which
    /// window owned the item, whether that window had been destroyed, or whether the item had
    /// already been removed — and so could suppress a correct registration after a window lifecycle
    /// change.
    /// <para>
    /// Ownership rules:
    /// <list type="bullet">
    /// <item>Each supported owner window has its own item on its own menu; a repeat registration by
    /// the same owner is an idempotent no-op, so no owner ever gets a duplicate item.</item>
    /// <item>A single menu can be owned by only one window: a second window that presents the same
    /// menu is refused (<see cref="MenuAttachStatus.SurfaceAlreadyOwned"/>), so the item is never
    /// added twice to one menu.</item>
    /// <item>Only <see cref="MenuSurfaceKind.ControlCenter"/> is accepted; any other window is
    /// refused without touching the menu.</item>
    /// <item>A registration for a window that is no longer alive is refused (fail closed).</item>
    /// <item>Removal drops every reference (item, owner, surface and the owner-close callback), so a
    /// destroyed window or menu is never retained through a static reference.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Thread safety: every entry point takes a single private lock, so repeated
    /// Attach/Remove/Clear calls from any thread are safe. Every surface mutation (add/remove) is
    /// performed while the lock is held, so two callers can never touch the same menu at once and
    /// the no-duplicate invariant holds under concurrency. Only the owner's removal callback runs
    /// after the lock is released, so a host cleanup handler can never deadlock the registry or
    /// re-enter it while it is held.
    /// </para>
    /// </summary>
    public sealed class MenuOwnershipRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<object, Entry> _byOwner = new Dictionary<object, Entry>(ReferenceComparer.Instance);
        private readonly Dictionary<IMenuSurface, Entry> _bySurface = new Dictionary<IMenuSurface, Entry>(ReferenceComparer.Instance);

        private sealed class Entry
        {
            public object Owner;
            public MenuSurfaceKind Kind;
            public IMenuSurface Surface;
            public string ItemId;
            public Action OnRemoved;
        }

        /// <summary>Number of owner windows currently holding an item.</summary>
        public int AttachedCount
        {
            get { lock (_gate) { return _byOwner.Count; } }
        }

        /// <summary>True when <paramref name="ownerKey"/> currently owns an item.</summary>
        public bool IsAttached(object ownerKey)
        {
            if (ownerKey == null)
            {
                return false;
            }

            lock (_gate)
            {
                return _byOwner.ContainsKey(ownerKey);
            }
        }

        /// <summary>The kind of the item owned by <paramref name="ownerKey"/>, or
        /// <see cref="MenuSurfaceKind.Unknown"/> when that window owns nothing.</summary>
        public MenuSurfaceKind OwnedKind(object ownerKey)
        {
            if (ownerKey == null)
            {
                return MenuSurfaceKind.Unknown;
            }

            lock (_gate)
            {
                Entry entry;
                return _byOwner.TryGetValue(ownerKey, out entry) ? entry.Kind : MenuSurfaceKind.Unknown;
            }
        }

        /// <summary>
        /// Registers the diagnostic item on <paramref name="surface"/> on behalf of
        /// <paramref name="ownerKey"/>. Returns true when the item is owned by that window after the
        /// call (freshly added, or already owned). Returns false — with <paramref name="status"/>
        /// explaining why — when nothing was registered.
        /// </summary>
        /// <param name="onRemoved">
        /// Optional host cleanup invoked when the item is removed (for example to detach a
        /// window-close subscription). It runs outside the registry lock and any exception it throws
        /// is contained.
        /// </param>
        public bool Attach(
            object ownerKey,
            MenuSurfaceKind kind,
            bool ownerAlive,
            IMenuSurface surface,
            string itemId,
            Action onClick,
            Action onRemoved,
            out MenuAttachStatus status)
        {
            if (ownerKey == null || surface == null || string.IsNullOrEmpty(itemId) || onClick == null)
            {
                status = MenuAttachStatus.SurfaceUnavailable;
                return false;
            }

            if (kind != MenuSurfaceKind.ControlCenter)
            {
                status = MenuAttachStatus.UnsupportedOwner;
                return false;
            }

            if (!ownerAlive)
            {
                status = MenuAttachStatus.OwnerNotAlive;
                return false;
            }

            lock (_gate)
            {
                // Idempotent: this window already owns its item.
                if (_byOwner.ContainsKey(ownerKey))
                {
                    status = MenuAttachStatus.AlreadyAttached;
                    return true;
                }

                // A single menu can be owned by only one window; refuse a second claimant so the
                // item is never added twice to one menu.
                if (_bySurface.ContainsKey(surface))
                {
                    status = MenuAttachStatus.SurfaceAlreadyOwned;
                    return false;
                }

                // Remove a stale item the surface may still hold under our id (for example one left
                // by an earlier registration whose entry was lost), so exactly one item exists.
                try
                {
                    if (surface.Contains(itemId))
                    {
                        surface.Remove(itemId);
                    }
                }
                catch (Exception)
                {
                    // A failing probe/removal must not mask the registration attempt below.
                }

                bool ok;
                try
                {
                    ok = surface.Add(itemId, onClick);
                }
                catch (Exception)
                {
                    ok = false;
                }

                if (!ok)
                {
                    status = MenuAttachStatus.AddFailed;
                    return false;
                }

                var entry = new Entry
                {
                    Owner = ownerKey,
                    Kind = kind,
                    Surface = surface,
                    ItemId = itemId,
                    OnRemoved = onRemoved
                };
                _byOwner[ownerKey] = entry;
                _bySurface[surface] = entry;
                status = MenuAttachStatus.Attached;
                return true;
            }
        }

        /// <summary>
        /// Removes the item owned by <paramref name="ownerKey"/>, if any. Returns true when an item
        /// was removed. Removal also drops the owner, surface and cleanup references.
        /// </summary>
        public bool Remove(object ownerKey)
        {
            if (ownerKey == null)
            {
                return false;
            }

            Entry entry;
            lock (_gate)
            {
                if (!_byOwner.TryGetValue(ownerKey, out entry))
                {
                    return false;
                }

                _byOwner.Remove(ownerKey);
                _bySurface.Remove(entry.Surface);

                // The surface mutation runs under the lock; the host callback runs after it.
                DetachSurfaceItem(entry);
            }

            RunRemovalCallback(entry);
            return true;
        }

        /// <summary>
        /// Removes every item and drops every reference. Returns the number of items that were
        /// registered. Used by a host restart and by tests.
        /// </summary>
        public int Clear()
        {
            List<Entry> entries;
            lock (_gate)
            {
                entries = new List<Entry>(_byOwner.Values);
                _byOwner.Clear();
                _bySurface.Clear();

                // Every surface mutation runs under the lock; the host callbacks run after it.
                foreach (var entry in entries)
                {
                    DetachSurfaceItem(entry);
                }
            }

            foreach (var entry in entries)
            {
                RunRemovalCallback(entry);
            }

            return entries.Count;
        }

        private static void DetachSurfaceItem(Entry entry)
        {
            if (entry == null || entry.Surface == null)
            {
                return;
            }

            try
            {
                entry.Surface.Remove(entry.ItemId);
            }
            catch (Exception)
            {
                // Removing the item must never throw out of the registry.
            }
        }

        private static void RunRemovalCallback(Entry entry)
        {
            if (entry == null)
            {
                return;
            }

            var onRemoved = entry.OnRemoved;
            if (onRemoved == null)
            {
                return;
            }

            try
            {
                onRemoved();
            }
            catch (Exception)
            {
                // Host cleanup is best-effort and must never throw out of the registry.
            }
        }

        /// <summary>Reference-identity comparer (works on net48 and net8.0; no BCL helper).</summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
