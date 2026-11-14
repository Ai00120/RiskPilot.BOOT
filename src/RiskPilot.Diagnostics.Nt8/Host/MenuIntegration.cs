using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RiskPilot.Diagnostics.Nt8.Core.Run;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Adds a "RiskPilot NT8 Diagnostic Center" item to the NinjaTrader Control Center main menu and
    /// routes it to the single diagnostic window. The menu item only ever opens the read-only
    /// diagnostic window; it never enables trading and never touches an account or connection.
    /// <para>
    /// Ownership is tracked explicitly by <see cref="MenuOwnershipRegistry"/> (host-free, unit
    /// tested): the registry remembers which window owns the item and on which menu it was placed,
    /// refuses any window that is not the Control Center, and removes the item and detaches its
    /// click handler when the owning window is destroyed. The previous global <c>bool _added</c>
    /// flag could not identify the owner or detect a destroyed window, so it could suppress a
    /// correct registration after a window lifecycle change.
    /// </para>
    /// <para>
    /// This file is part of the real-host build only (see the csproj). Every entry point is
    /// contained: it never throws to the caller, and every host callback is guarded.
    /// </para>
    /// </summary>
    public static class MenuIntegration
    {
        /// <summary>The stable id of the diagnostic menu item.</summary>
        public const string MenuItemId = DiagnosticMenu.ItemId;

        private static readonly MenuOwnershipRegistry Registry = new MenuOwnershipRegistry();
        private static readonly object Gate = new object();
        private static readonly List<KeyValuePair<Window, EventHandler>> CloseHandlers =
            new List<KeyValuePair<Window, EventHandler>>();

        /// <summary>
        /// Registers the diagnostic menu item on the given window's main menu. Returns false when
        /// the window is not a supported owner (only the Control Center is), is no longer alive, or
        /// has no menu; this is contained and never thrown to the caller. A repeated call for the
        /// same owner is an idempotent no-op and never adds a second item.
        /// </summary>
        public static bool Attach(Window controlCenterWindow, DiagnosticCenterWindowManager manager)
        {
            if (controlCenterWindow == null || manager == null)
            {
                return false;
            }

            try
            {
                if (ClassifyWindow(controlCenterWindow) != MenuSurfaceKind.ControlCenter)
                {
                    return false;
                }

                if (!IsOwnerAlive(controlCenterWindow))
                {
                    return false;
                }

                var menu = FindMainMenu(controlCenterWindow);
                if (menu == null)
                {
                    return false;
                }

                var owner = (object)controlCenterWindow;
                var surface = new WpfMenuSurface(menu);
                Action onClick = () => manager.Show(controlCenterWindow.Dispatcher);

                // Serialise the registry entry and its close subscription under one lock, so a
                // concurrent Reset can never leave a dangling subscription (which would retain the
                // window) or a registry entry with no subscription (which would retain the menu).
                lock (Gate)
                {
                    MenuAttachStatus status;
                    var attached = Registry.Attach(
                        owner,
                        MenuSurfaceKind.ControlCenter,
                        true,
                        surface,
                        MenuItemId,
                        onClick,
                        () => UnsubscribeCloseLocked(owner),
                        out status);

                    if (!attached)
                    {
                        return false;
                    }

                    EnsureCloseSubscribedLocked(controlCenterWindow);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Removes the item owned by the given window, if any, and detaches its handlers. Returns
        /// true when an item was removed. Safe to call repeatedly and from any thread.
        /// </summary>
        public static bool Detach(Window controlCenterWindow)
        {
            if (controlCenterWindow == null)
            {
                return false;
            }

            try
            {
                lock (Gate)
                {
                    return Registry.Remove(controlCenterWindow);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Removes every menu item and drops every retained window/menu reference. Used by tests
        /// and by a host restart. Safe to call repeatedly.
        /// </summary>
        public static void Reset()
        {
            lock (Gate)
            {
                try
                {
                    // Registry.Clear runs each owner's removal callback (UnsubscribeCloseLocked),
                    // which drains CloseHandlers as it goes.
                    Registry.Clear();
                }
                catch (Exception)
                {
                    // Resetting must never throw; a leftover entry is retried on the next call.
                }

                // Defensive drain: Registry.Clear already detaches each subscription, so this
                // normally finds nothing.
                var handlers = new List<KeyValuePair<Window, EventHandler>>(CloseHandlers);
                CloseHandlers.Clear();
                foreach (var pair in handlers)
                {
                    try
                    {
                        pair.Key.Closed -= pair.Value;
                    }
                    catch (Exception)
                    {
                        // Unsubscribing during teardown must never throw.
                    }
                }
            }
        }

        /// <summary>
        /// Subscribes to the owning window's <c>Closed</c> event so the item is removed when the
        /// window is destroyed. Must be called with <see cref="Gate"/> held. Idempotent per window.
        /// </summary>
        private static void EnsureCloseSubscribedLocked(Window window)
        {
            EventHandler existing;
            if (TryGetCloseHandler(window, out existing))
            {
                return;
            }

            // The window raises Closed on its own thread without Gate held, so take the lock here;
            // the registry's removal callback (UnsubscribeCloseLocked) then runs under Gate too.
            EventHandler handler = (_, __) => { lock (Gate) { Registry.Remove(window); } };
            CloseHandlers.Add(new KeyValuePair<Window, EventHandler>(window, handler));
            window.Closed += handler;
        }

        /// <summary>
        /// Detaches the owning window's close subscription. Invoked from the registry's removal
        /// callback, which only ever runs while <see cref="Gate"/> is held (every registry call in
        /// this class is made under the lock), so it does not take the lock again.
        /// </summary>
        private static void UnsubscribeCloseLocked(object owner)
        {
            var window = owner as Window;
            if (window == null)
            {
                return;
            }

            for (var i = 0; i < CloseHandlers.Count; i++)
            {
                if (ReferenceEquals(CloseHandlers[i].Key, window))
                {
                    var handler = CloseHandlers[i].Value;
                    CloseHandlers.RemoveAt(i);
                    try
                    {
                        window.Closed -= handler;
                    }
                    catch (Exception)
                    {
                        // Detaching during teardown must never throw.
                    }

                    return;
                }
            }
        }

        private static bool TryGetCloseHandler(Window window, out EventHandler handler)
        {
            for (var i = 0; i < CloseHandlers.Count; i++)
            {
                if (ReferenceEquals(CloseHandlers[i].Key, window))
                {
                    handler = CloseHandlers[i].Value;
                    return true;
                }
            }

            handler = null;
            return false;
        }

        private static MenuSurfaceKind ClassifyWindow(Window window)
        {
            if (window == null)
            {
                return MenuSurfaceKind.Unknown;
            }

            // Feed the host-free classifier the type hierarchy (leaf first) as type names, so the
            // "which windows may carry the item" decision is unit tested without WPF.
            var names = new List<string>();
            var type = window.GetType();
            while (type != null)
            {
                if (!string.IsNullOrEmpty(type.FullName))
                {
                    names.Add(type.FullName);
                }

                names.Add(type.Name);
                type = type.BaseType;
            }

            return MenuOwnerClassifier.Classify(names);
        }

        private static bool IsOwnerAlive(Window window)
        {
            var dispatcher = window.Dispatcher;
            if (dispatcher == null)
            {
                return false;
            }

            return !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished;
        }

        private static Menu FindMainMenu(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is Menu menu)
                {
                    return menu;
                }

                var nested = FindMainMenu(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        /// <summary>
        /// Read-only view over a WPF <see cref="Menu"/>. It is strictly additive: it can add and
        /// remove the one diagnostic item and report whether it is present, and nothing else. It
        /// never submits, changes or cancels an order and never mutates an account or connection.
        /// </summary>
        private sealed class WpfMenuSurface : IMenuSurface
        {
            private readonly Menu _menu;
            private MenuItem _item;
            private RoutedEventHandler _handler;

            public WpfMenuSurface(Menu menu)
            {
                _menu = menu;
            }

            public int ItemCount
            {
                get { return _menu.Items.Count; }
            }

            public bool Contains(string itemId)
            {
                if (Matches(_item, itemId))
                {
                    return true;
                }

                foreach (var obj in _menu.Items)
                {
                    if (Matches(obj as MenuItem, itemId))
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool Add(string itemId, Action onClick)
            {
                if (string.IsNullOrEmpty(itemId) || onClick == null)
                {
                    return false;
                }

                // Never add a second item with the same id.
                if (Contains(itemId))
                {
                    return false;
                }

                _handler = (_, __) => onClick();
                _item = new MenuItem
                {
                    Header = Core.Identity.BuildIdentity.ProductName,
                    Name = itemId
                };
                _item.Click += _handler;
                _menu.Items.Add(_item);
                return true;
            }

            public bool Remove(string itemId)
            {
                var removed = false;

                if (Matches(_item, itemId))
                {
                    if (_handler != null)
                    {
                        _item.Click -= _handler;
                    }

                    _menu.Items.Remove(_item);
                    _item = null;
                    _handler = null;
                    removed = true;
                }

                // Also drop any item the menu still holds under our id (for example one left by an
                // earlier registration), so exactly one item can ever exist.
                for (var i = _menu.Items.Count - 1; i >= 0; i--)
                {
                    if (Matches(_menu.Items[i] as MenuItem, itemId))
                    {
                        _menu.Items.RemoveAt(i);
                        removed = true;
                    }
                }

                return removed;
            }

            private static bool Matches(MenuItem item, string itemId)
            {
                return item != null
                    && !string.IsNullOrEmpty(itemId)
                    && string.Equals(item.Name, itemId, StringComparison.Ordinal);
            }
        }
    }
}
