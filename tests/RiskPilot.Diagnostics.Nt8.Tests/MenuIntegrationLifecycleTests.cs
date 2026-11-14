using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Lifecycle tests for the diagnostic menu-item ownership tracking. The production
    /// <see cref="MenuOwnershipRegistry"/> and <see cref="MenuOwnerClassifier"/> are host-free, so
    /// they are exercised here over an in-memory <see cref="IMenuSurface"/> (the same seam the
    /// real-host adapter implements over a WPF <c>Menu</c>).
    /// <para>
    /// These tests cover the reported defect: the old global <c>bool _added</c> flag could not tell
    /// which window owned the item, whether that window had been destroyed, or whether the item had
    /// already been removed. They assert explicit ownership, idempotent registration, correct owner
    /// selection, removal on owner destruction, handler cleanup, reset-then-reattach and the
    /// no-duplicate guarantee.
    /// </para>
    /// </summary>
    public class MenuIntegrationLifecycleTests
    {
        private const string ItemId = DiagnosticMenu.ItemId;

        private static MenuAttachResult Attach(
            MenuOwnershipRegistry registry,
            object owner,
            IMenuSurface surface,
            Action onClick = null,
            Action onRemoved = null,
            MenuSurfaceKind kind = MenuSurfaceKind.ControlCenter,
            bool ownerAlive = true)
        {
            MenuAttachStatus status;
            var attached = registry.Attach(
                owner,
                kind,
                ownerAlive,
                surface,
                ItemId,
                onClick ?? (() => { }),
                onRemoved,
                out status);
            return new MenuAttachResult(attached, status);
        }

        [Fact]
        public void FirstRegistration_AddsExactlyOneItem_AndOwnsIt()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();

            var result = Attach(registry, owner, surface);

            Assert.True(result.Attached);
            Assert.Equal(MenuAttachStatus.Attached, result.Status);
            Assert.Equal(1, surface.ItemCount);
            Assert.True(surface.Contains(ItemId));
            Assert.True(registry.IsAttached(owner));
            Assert.Equal(1, registry.AttachedCount);
            Assert.Equal(MenuSurfaceKind.ControlCenter, registry.CurrentKind);
        }

        [Fact]
        public void RepeatedRegistration_IsIdempotent_AndNeverDuplicatesTheItem()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();

            Assert.True(Attach(registry, owner, surface).Attached);
            var second = Attach(registry, owner, surface);
            var third = Attach(registry, owner, surface);

            Assert.True(second.Attached);
            Assert.Equal(MenuAttachStatus.AlreadyAttached, second.Status);
            Assert.True(third.Attached);
            Assert.Equal(MenuAttachStatus.AlreadyAttached, third.Status);

            // The surface saw exactly one Add call and still holds exactly one item.
            Assert.Equal(1, surface.AddCalls);
            Assert.Equal(1, surface.ItemCount);
            Assert.Equal(1, registry.AttachedCount);
        }

        [Fact]
        public void SecondOwner_TakesOver_AndThePreviousItemIsRemoved_NoDuplicate()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var first = new object();
            var second = new object();
            var firstCleanupCount = 0;

            Assert.True(Attach(registry, first, surface, onRemoved: () => firstCleanupCount++).Attached);
            var result = Attach(registry, second, surface);

            Assert.True(result.Attached);
            Assert.Equal(MenuAttachStatus.Replaced, result.Status);
            Assert.Equal(1, surface.ItemCount);
            Assert.False(registry.IsAttached(first));
            Assert.True(registry.IsAttached(second));
            Assert.Equal(1, registry.AttachedCount);
            // The displaced owner's cleanup ran exactly once.
            Assert.Equal(1, firstCleanupCount);
        }

        [Fact]
        public void UnsupportedOwner_IsRefused_AndNeverTouchesTheSurface()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();

            var result = Attach(registry, owner, surface, kind: MenuSurfaceKind.Other);

            Assert.False(result.Attached);
            Assert.Equal(MenuAttachStatus.UnsupportedOwner, result.Status);
            Assert.Equal(0, surface.ItemCount);
            Assert.Equal(0, surface.AddCalls);
            Assert.False(registry.IsAttached(owner));
        }

        [Fact]
        public void DeadOwner_IsRefused_FailClosed()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();

            var result = Attach(registry, new object(), surface, ownerAlive: false);

            Assert.False(result.Attached);
            Assert.Equal(MenuAttachStatus.OwnerNotAlive, result.Status);
            Assert.Equal(0, surface.ItemCount);
        }

        [Fact]
        public void SurfaceAddFailure_IsReportedAsAddFailed_AndNothingIsOwned()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface { FailAdd = true };
            var owner = new object();

            var result = Attach(registry, owner, surface);

            Assert.False(result.Attached);
            Assert.Equal(MenuAttachStatus.AddFailed, result.Status);
            Assert.False(registry.IsAttached(owner));
            Assert.Equal(0, registry.AttachedCount);
        }

        [Fact]
        public void StaleSurfaceItem_IsReplaced_SoOnlyOneItemExists()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            // A leftover item from an earlier registration the registry no longer tracks.
            surface.Seed(ItemId);
            var owner = new object();

            var result = Attach(registry, owner, surface);

            Assert.True(result.Attached);
            Assert.Equal(1, surface.ItemCount);
        }

        [Fact]
        public void Remove_RemovesTheItem_AndDropsOwnership()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();
            var cleanupCount = 0;

            Assert.True(Attach(registry, owner, surface, onRemoved: () => cleanupCount++).Attached);
            Assert.True(registry.Remove(owner));

            Assert.Equal(0, surface.ItemCount);
            Assert.False(registry.IsAttached(owner));
            Assert.Equal(0, registry.AttachedCount);
            Assert.Equal(MenuSurfaceKind.Unknown, registry.CurrentKind);
            Assert.Equal(1, cleanupCount);
        }

        [Fact]
        public void Remove_IsIdempotent_AndDoesNotRerunCleanup()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();
            var cleanupCount = 0;

            Assert.True(Attach(registry, owner, surface, onRemoved: () => cleanupCount++).Attached);
            Assert.True(registry.Remove(owner));
            Assert.False(registry.Remove(owner));
            Assert.False(registry.Remove(owner));

            Assert.Equal(1, cleanupCount);
        }

        [Fact]
        public void OwnerDestroyed_RemovesTheItem_AndRunsHandlerCleanup()
        {
            // Models the host close handler: the owner window is destroyed and its close callback
            // removes the item and detaches the handler. A destroyed window must leave nothing
            // behind.
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();
            var handlerAttached = true;
            var handlerDetached = 0;

            Action onRemoved = () =>
            {
                handlerDetached++;
                handlerAttached = false;
            };

            Assert.True(Attach(registry, owner, surface, onRemoved: onRemoved).Attached);

            // Simulate the owning window being destroyed.
            registry.Remove(owner);

            Assert.False(handlerAttached);
            Assert.Equal(1, handlerDetached);
            Assert.Equal(0, surface.ItemCount);
            Assert.False(registry.IsAttached(owner));
        }

        [Fact]
        public void RemovedItem_NoLongerRaisesItsClickHandler()
        {
            // Event-handler cleanup: once removed, the item (and its captured manager/window) must
            // be gone from the surface, so a later click can never reach it.
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();
            var clicks = 0;

            Assert.True(Attach(registry, owner, surface, onClick: () => clicks++).Attached);
            Assert.True(registry.Remove(owner));

            Assert.False(surface.TryClick(ItemId));
            Assert.Equal(0, clicks);
            Assert.Equal(0, surface.ItemCount);
        }

        [Fact]
        public void Reset_NoLongerRaisesItsClickHandler()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var clicks = 0;

            Assert.True(Attach(registry, new object(), surface, onClick: () => clicks++).Attached);
            registry.Clear();

            Assert.False(surface.TryClick(ItemId));
            Assert.Equal(0, clicks);
        }

        [Fact]
        public void Clear_RemovesEveryItem_AndRunsEveryCleanupOnce()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var cleanupCount = 0;

            Assert.True(Attach(registry, new object(), surface, onRemoved: () => cleanupCount++).Attached);

            Assert.Equal(1, registry.Clear());
            Assert.Equal(0, surface.ItemCount);
            Assert.Equal(0, registry.AttachedCount);
            Assert.Equal(1, cleanupCount);

            // A second clear is a no-op and does not rerun cleanup.
            Assert.Equal(0, registry.Clear());
            Assert.Equal(1, cleanupCount);
        }

        [Fact]
        public void ResetThenReattach_RegistersAgain()
        {
            // Regression: the old global flag stayed "added" and could suppress a correct
            // registration after a window lifecycle change (host restart / AddOn reload).
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();

            Assert.True(Attach(registry, owner, surface).Attached);
            Assert.Equal(1, surface.ItemCount);

            registry.Clear(); // Reset
            Assert.Equal(0, surface.ItemCount);
            Assert.Equal(0, registry.AttachedCount);

            var result = Attach(registry, owner, surface);
            Assert.True(result.Attached);
            Assert.Equal(MenuAttachStatus.Attached, result.Status);
            Assert.Equal(1, surface.ItemCount);
            Assert.True(registry.IsAttached(owner));
        }

        [Fact]
        public void NewOwnerAfterReset_RegistersCleanly()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();

            Assert.True(Attach(registry, new object(), surface).Attached);
            registry.Clear();

            var result = Attach(registry, new object(), surface);
            Assert.True(result.Attached);
            Assert.Equal(1, surface.ItemCount);
        }

        [Fact]
        public void ClickCommand_IsPreserved_AfterRegistration()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();
            var clicks = 0;

            Assert.True(Attach(registry, owner, surface, onClick: () => clicks++).Attached);

            Assert.True(surface.TryClick(ItemId));
            Assert.True(surface.TryClick(ItemId));
            Assert.Equal(2, clicks);
        }

        [Fact]
        public void NullArguments_AreRefused()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();

            MenuAttachStatus status;
            Assert.False(registry.Attach(null, MenuSurfaceKind.ControlCenter, true, surface, ItemId, () => { }, null, out status));
            Assert.False(registry.Attach(new object(), MenuSurfaceKind.ControlCenter, true, null, ItemId, () => { }, null, out status));
            Assert.False(registry.Attach(new object(), MenuSurfaceKind.ControlCenter, true, surface, null, () => { }, null, out status));
            Assert.False(registry.Attach(new object(), MenuSurfaceKind.ControlCenter, true, surface, ItemId, null, null, out status));
            Assert.Equal(0, surface.ItemCount);
        }

        [Fact]
        public void RepeatedAttachDetachReset_IsSafe()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owner = new object();

            for (var i = 0; i < 5; i++)
            {
                Assert.True(Attach(registry, owner, surface).Attached);
                Assert.True(registry.Remove(owner));
                Assert.False(registry.Remove(owner));
                registry.Clear();
            }

            Assert.Equal(0, surface.ItemCount);
            Assert.Equal(0, registry.AttachedCount);
        }

        [Fact]
        public void ConcurrentAttachRemoveClear_NeverLeavesMoreThanOneItem_AndNeverThrows()
        {
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var owners = new object[4];
            for (var i = 0; i < owners.Length; i++)
            {
                owners[i] = new object();
            }

            Parallel.For(0, 2000, i =>
            {
                var owner = owners[i % owners.Length];
                switch (i % 3)
                {
                    case 0:
                        Attach(registry, owner, surface);
                        break;
                    case 1:
                        registry.Remove(owner);
                        break;
                    default:
                        registry.Clear();
                        break;
                }
            });

            Assert.True(surface.ItemCount <= 1);
            Assert.True(registry.AttachedCount <= 1);
            // AttachedCount and the live surface item must agree: no entry without an item and no
            // item without an entry.
            Assert.Equal(registry.AttachedCount, surface.ItemCount);
        }

        [Fact]
        public void ConcurrentAttachFromDifferentOwners_LeavesExactlyOneItem()
        {
            // Two windows racing for the single item must never both succeed: the surface ends with
            // exactly one item and at most one owner is recorded.
            var registry = new MenuOwnershipRegistry();
            var surface = new FakeMenuSurface();
            var first = new object();
            var second = new object();

            Parallel.Invoke(
                () => Attach(registry, first, surface),
                () => Attach(registry, second, surface));

            Assert.Equal(1, surface.ItemCount);
            Assert.Equal(1, registry.AttachedCount);
        }

        [Theory]
        [InlineData("NinjaTrader.Gui.ControlCenter", MenuSurfaceKind.ControlCenter)]
        [InlineData("ControlCenter", MenuSurfaceKind.ControlCenter)]
        [InlineData("NinjaTrader.Gui.SuperDom.SuperDom", MenuSurfaceKind.Other)]
        [InlineData("Order", MenuSurfaceKind.Other)]
        public void OwnerClassifier_SelectsOnlyTheControlCenter(string typeName, MenuSurfaceKind expected)
        {
            var names = new List<string> { typeName };
            Assert.Equal(expected, MenuOwnerClassifier.Classify(names));
        }

        [Fact]
        public void OwnerClassifier_MatchesAnAncestorControlCenter()
        {
            // A derived window type still resolves to the Control Center owner through its base.
            var names = new List<string> { "MyCustomControlCenter", "ControlCenter", "NTWindow" };
            Assert.Equal(MenuSurfaceKind.ControlCenter, MenuOwnerClassifier.Classify(names));
        }

        [Fact]
        public void OwnerClassifier_NullOrEmpty_IsUnknownOrOther()
        {
            Assert.Equal(MenuSurfaceKind.Unknown, MenuOwnerClassifier.Classify(null));
            Assert.Equal(MenuSurfaceKind.Other, MenuOwnerClassifier.Classify(new List<string>()));
        }

        [Fact]
        public void MenuItemId_IsStable()
        {
            Assert.Equal("RiskPilotDiagnosticsNt8CenterMenuItem", DiagnosticMenu.ItemId);
        }

        /// <summary>
        /// In-memory <see cref="IMenuSurface"/> that mirrors the real adapter's contract: it holds
        /// items by id, records add/remove calls, can be forced to fail an add, and can raise the
        /// click command. It is deliberately minimal so a test can assert exactly what the registry
        /// asked the surface to do.
        /// </summary>
        private sealed class FakeMenuSurface : IMenuSurface
        {
            private readonly object _gate = new object();
            private readonly Dictionary<string, Action> _items = new Dictionary<string, Action>(StringComparer.Ordinal);

            public int AddCalls;
            public int RemoveCalls;
            public bool FailAdd;

            public int ItemCount
            {
                get { lock (_gate) { return _items.Count; } }
            }

            public bool Contains(string itemId)
            {
                lock (_gate)
                {
                    return itemId != null && _items.ContainsKey(itemId);
                }
            }

            public bool Add(string itemId, Action onClick)
            {
                lock (_gate)
                {
                    AddCalls++;
                    if (FailAdd || itemId == null || onClick == null || _items.ContainsKey(itemId))
                    {
                        return false;
                    }

                    _items[itemId] = onClick;
                    return true;
                }
            }

            public bool Remove(string itemId)
            {
                lock (_gate)
                {
                    RemoveCalls++;
                    return itemId != null && _items.Remove(itemId);
                }
            }

            public void Seed(string itemId)
            {
                lock (_gate)
                {
                    _items[itemId] = () => { };
                }
            }

            public bool TryClick(string itemId)
            {
                Action action;
                lock (_gate)
                {
                    if (itemId == null || !_items.TryGetValue(itemId, out action))
                    {
                        return false;
                    }
                }

                action();
                return true;
            }
        }
    }
}
