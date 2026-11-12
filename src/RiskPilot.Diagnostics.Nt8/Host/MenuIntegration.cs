using System;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using RiskPilot.Diagnostics.Nt8.Core.Run;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Adds a "RiskPilot NT8 Diagnostic Center" item to the NinjaTrader Control Center menu and
    /// routes it to the single diagnostic window. The menu item only ever opens the read-only
    /// diagnostic window; it never enables trading and never touches an account or connection.
    /// <para>
    /// This file is part of the real-host build only (see the csproj). The menu item id is a
    /// stable string so it is never added twice.
    /// </para>
    /// </summary>
    public static class MenuIntegration
    {
        public const string MenuItemId = "RiskPilotDiagnosticsNt8CenterMenuItem";

        private static readonly object Gate = new object();
        private static bool _added;

        /// <summary>
        /// Adds the menu item once. A second call is a no-op. Returns false when the host menu
        /// could not be located; this is contained and never thrown to the caller.
        /// </summary>
        public static bool Attach(Window controlCenterWindow, DiagnosticCenterWindowManager manager)
        {
            if (controlCenterWindow == null || manager == null)
            {
                return false;
            }

            lock (Gate)
            {
                if (_added)
                {
                    return true;
                }

                try
                {
                    var menu = FindMainMenu(controlCenterWindow);
                    if (menu == null)
                    {
                        return false;
                    }

                    var item = new MenuItem
                    {
                        Header = Core.Identity.BuildIdentity.ProductName,
                        Name = MenuItemId
                    };

                    item.Click += (_, __) => manager.Show(controlCenterWindow.Dispatcher);
                    menu.Items.Add(item);
                    _added = true;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Resets the "already added" flag. Used by tests and by a host restart.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                _added = false;
            }
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
    }
}
