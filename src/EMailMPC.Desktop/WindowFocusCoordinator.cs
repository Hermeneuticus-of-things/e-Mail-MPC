using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using EMailMPC.Core.Interaction;

namespace EMailMPC.Desktop;

public sealed class WindowFocusCoordinator : IDisposable
{
    private static readonly ConditionalWeakTable<Window, WindowFocusCoordinator> Coordinators = new();
    private readonly Window _window;
    private readonly Dictionary<string, SurfaceBinding> _bindings = new(StringComparer.Ordinal);
    private bool _disposed;

    private WindowFocusCoordinator(
        Window window,
        string windowId,
        IReadOnlyDictionary<string, Func<object?, bool>>? windowCommands)
    {
        _window = window;
        State = new FocusState(windowId, windowCommands);
        Keyboard.AddGotKeyboardFocusHandler(_window, OnGotKeyboardFocus);
    }

    public FocusState State { get; }

    public static WindowFocusCoordinator Attach(
        Window window,
        string windowId,
        IReadOnlyDictionary<string, Func<object?, bool>>? windowCommands = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Coordinators.TryGetValue(window, out var existing))
        {
            return existing;
        }

        var coordinator = new WindowFocusCoordinator(window, windowId, windowCommands);
        Coordinators.Add(window, coordinator);
        window.Closed += coordinator.Window_Closed;
        return coordinator;
    }

    public void RegisterSurface(
        string surfaceId,
        FrameworkElement root,
        FrameworkElement? initialFocus = null,
        string? parentId = null,
        bool modal = false,
        IReadOnlyDictionary<string, Func<object?, bool>>? commands = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);
        if (_bindings.ContainsKey(surfaceId))
        {
            throw new InvalidOperationException($"WPF focus surface '{surfaceId}' is already registered.");
        }

        var registration = new FocusSurfaceRegistration
        {
            Id = surfaceId,
            Level = FocusLevel.Surface,
            ParentId = parentId ?? State.WindowSurfaceId,
            Modal = modal,
            InitialFocus = initialFocus is null ? null : () => GetControlId(surfaceId, initialFocus),
            Commands = commands ?? new Dictionary<string, Func<object?, bool>>(StringComparer.Ordinal)
        };
        State.Register(registration);
        _bindings.Add(surfaceId, new SurfaceBinding(surfaceId, root, initialFocus, registration.ParentId!));
    }

    public void ActivateInitialSurface(string surfaceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State.ActiveEntry.SurfaceId == State.WindowSurfaceId)
        {
            State.PushFocus(surfaceId);
        }
        else
        {
            State.SwitchFocus(surfaceId);
        }

        RestoreFocus(surfaceId);
    }

    public IDisposable PushModalSurface(string surfaceId)
        => PushDynamicSurface(surfaceId, State.WindowSurfaceId, modal: true);

    public IDisposable PushTransientSurface(string surfaceId, string parentId, string? triggerId)
        => PushDynamicSurface(surfaceId, parentId, modal: false, triggerId: triggerId);

    private IDisposable PushDynamicSurface(
        string surfaceId,
        string parentId,
        bool modal,
        string? triggerId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_bindings.ContainsKey(surfaceId))
        {
            throw new InvalidOperationException($"Focus surface '{surfaceId}' is already registered.");
        }

        var current = Keyboard.FocusedElement as FrameworkElement;
        if (current is not null)
        {
            RecordFocusedElement(current);
        }

        State.Register(new FocusSurfaceRegistration
        {
            Id = surfaceId,
            Level = FocusLevel.Surface,
            ParentId = parentId,
            Modal = modal
        });
        _bindings.Add(surfaceId, new SurfaceBinding(surfaceId, null, null, parentId));
        State.PushFocus(surfaceId, triggerId ?? State.ActiveEntry.LastFocusedControlId);
        return new SurfaceScope(this, surfaceId);
    }

    public bool Dispatch(string commandId, object? arguments = null) =>
        !_disposed && State.Dispatch(commandId, arguments);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Keyboard.RemoveGotKeyboardFocusHandler(_window, OnGotKeyboardFocus);
        foreach (var binding in _bindings.Values.Reverse())
        {
            if (State.RegisteredSurfaceIds.Contains(binding.Id))
            {
                State.Unregister(binding.Id);
            }
        }

        _bindings.Clear();
        Coordinators.Remove(_window);
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_disposed || e.NewFocus is not DependencyObject focused)
        {
            return;
        }

        if (e.OldFocus is FrameworkElement previousElement)
        {
            RecordFocusedElement(previousElement);
        }

        var binding = FindBinding(focused);
        if (State.HasActiveModal)
        {
            return;
        }

        if (binding is null || string.Equals(State.ActiveEntry.SurfaceId, binding.Id, StringComparison.Ordinal))
        {
            if (binding is not null && focused is FrameworkElement element)
            {
                RecordFocusedElement(element);
            }

            return;
        }

        if (State.Stack.Any(entry => string.Equals(entry.SurfaceId, binding.ParentId, StringComparison.Ordinal)) &&
            string.Equals(State.ActiveEntry.SurfaceId, binding.ParentId, StringComparison.Ordinal))
        {
            State.PushFocus(binding.Id, State.ActiveEntry.LastFocusedControlId);
        }
        else if (State.Stack.Any(entry => string.Equals(entry.SurfaceId, binding.ParentId, StringComparison.Ordinal)))
        {
            State.SwitchFocus(binding.Id);
        }
        else
        {
            return;
        }

        if (focused is FrameworkElement focusedElement)
        {
            RecordFocusedElement(focusedElement);
        }
    }

    private SurfaceBinding? FindBinding(DependencyObject target)
    {
        SurfaceBinding? match = null;
        var nearestDistance = int.MaxValue;
        var distance = 0;
        for (DependencyObject? current = target; current is not null; current = GetParent(current), distance++)
        {
            foreach (var binding in _bindings.Values)
            {
                if (ReferenceEquals(binding.Root, current) && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    match = binding;
                }
            }
        }

        return match;
    }

    private void RecordFocusedElement(FrameworkElement element)
    {
        var binding = FindBinding(element);
        if (binding is null || !string.Equals(State.ActiveEntry.SurfaceId, binding.Id, StringComparison.Ordinal))
        {
            return;
        }

        binding.LastFocusedElement = element;
        var textBox = element as TextBox;
        var scrollViewer = FindAncestor<ScrollViewer>(element);
        var selectedItem = element is TextBox
            ? new TextSelection(textBox!.SelectionStart, textBox.SelectionLength)
            : GetSelectedItem(element);
        var controlId = GetControlId(binding.Id, element);
        State.RecordActivePosition(
            controlId,
            textBox?.SelectionStart,
            textBox is null ? null : textBox.SelectionStart + textBox.SelectionLength,
            scrollViewer?.HorizontalOffset,
            scrollViewer?.VerticalOffset,
            selectedItem);
        binding.Snapshot = new ControlSnapshot(
            element,
            textBox?.SelectionStart,
            textBox?.SelectionLength,
            scrollViewer?.HorizontalOffset,
            scrollViewer?.VerticalOffset);
    }

    private void RestoreFocus(string surfaceId)
    {
        if (!_bindings.TryGetValue(surfaceId, out var binding) || binding.Root is null)
        {
            return;
        }

        var snapshot = binding.Snapshot;
        var target = snapshot?.Element;
        if (target is null || !target.IsVisible || !target.IsEnabled || !target.Focusable)
        {
            target = binding.InitialFocus is { IsVisible: true, IsEnabled: true, Focusable: true } initial
                ? initial
                : binding.Root;
        }

        _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!target.IsVisible || !target.IsEnabled)
            {
                return;
            }

            target.Focus();
            if (target is TextBox textBox && snapshot is { SelectionStart: not null, SelectionLength: not null })
            {
                var start = Math.Clamp(snapshot.SelectionStart.Value, 0, textBox.Text.Length);
                var length = Math.Clamp(snapshot.SelectionLength.Value, 0, textBox.Text.Length - start);
                textBox.Select(start, length);
            }

            var scrollViewer = FindAncestor<ScrollViewer>(target);
            if (scrollViewer is not null && snapshot?.VerticalOffset is { } verticalOffset)
            {
                scrollViewer.ScrollToVerticalOffset(verticalOffset);
                if (snapshot.HorizontalOffset is { } horizontalOffset)
                {
                    scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                }
            }
        });
    }

    private void PopDynamicSurface(string surfaceId)
    {
        if (_disposed || !_bindings.ContainsKey(surfaceId))
        {
            return;
        }

        State.PopFocus(surfaceId);
        _bindings.Remove(surfaceId);
        State.Unregister(surfaceId);
        RestoreFocus(State.ActiveEntry.SurfaceId);
    }

    private static string GetControlId(string surfaceId, FrameworkElement element) =>
        string.IsNullOrWhiteSpace(element.Name)
            ? $"{surfaceId}.{element.GetType().Name}"
            : $"{surfaceId}.{element.Name}";

    private static object? GetSelectedItem(FrameworkElement element) =>
        element switch
        {
            DataGrid grid => grid.SelectedItem,
            ListBox list => list.SelectedItem,
            _ => FindAncestor<DataGrid>(element)?.SelectedItem ?? FindAncestor<ListBox>(element)?.SelectedItem
        };

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = GetParent(element); current is not null; current = GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is Visual or Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(element);
            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return element switch
        {
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(element)
        };
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    private sealed class SurfaceScope(WindowFocusCoordinator owner, string surfaceId) : IDisposable
    {
        private WindowFocusCoordinator? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.PopDynamicSurface(surfaceId);
        }
    }

    private sealed class SurfaceBinding(
        string id,
        FrameworkElement? root,
        FrameworkElement? initialFocus,
        string parentId)
    {
        public string Id { get; } = id;

        public string ParentId { get; } = parentId;

        public FrameworkElement? Root { get; } = root;

        public FrameworkElement? InitialFocus { get; } = initialFocus;

        public FrameworkElement? LastFocusedElement { get; set; }

        public ControlSnapshot? Snapshot { get; set; }
    }

    private sealed record ControlSnapshot(
        FrameworkElement Element,
        int? SelectionStart,
        int? SelectionLength,
        double? HorizontalOffset,
        double? VerticalOffset);

    private sealed record TextSelection(int Start, int Length);
}
