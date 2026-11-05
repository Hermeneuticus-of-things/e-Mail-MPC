namespace EMailMPC.Core.Interaction;

public enum FocusLevel
{
    App,
    Window,
    Surface,
    Object
}

public sealed class FocusEntry
{
    internal FocusEntry(string surfaceId, FocusLevel level, string? triggerId)
    {
        SurfaceId = surfaceId;
        Level = level;
        TriggerId = triggerId;
    }

    public string SurfaceId { get; }

    public FocusLevel Level { get; }

    public string? TriggerId { get; }

    public string? LastFocusedControlId { get; private set; }

    public int? CaretStart { get; private set; }

    public int? CaretEnd { get; private set; }

    public double? ScrollX { get; private set; }

    public double? ScrollY { get; private set; }

    public object? Selection { get; private set; }

    internal void RecordPosition(
        string? controlId,
        int? caretStart,
        int? caretEnd,
        double? scrollX,
        double? scrollY,
        object? selection)
    {
        LastFocusedControlId = controlId;
        CaretStart = caretStart;
        CaretEnd = caretEnd;
        ScrollX = scrollX;
        ScrollY = scrollY;
        Selection = selection;
    }

    internal FocusEntry CopyForReactivation(string? triggerId) =>
        new(SurfaceId, Level, triggerId)
        {
            LastFocusedControlId = LastFocusedControlId,
            CaretStart = CaretStart,
            CaretEnd = CaretEnd,
            ScrollX = ScrollX,
            ScrollY = ScrollY,
            Selection = Selection
        };
}

public sealed class FocusSurfaceRegistration
{
    public required string Id { get; init; }

    public required FocusLevel Level { get; init; }

    public required string? ParentId { get; init; }

    public bool Modal { get; init; }

    public Func<string?>? InitialFocus { get; init; }

    public IReadOnlyDictionary<string, Func<object?, bool>> Commands { get; init; } =
        new Dictionary<string, Func<object?, bool>>(StringComparer.Ordinal);

    public Action<FocusEntry>? OnFocus { get; init; }

    public Action<FocusEntry>? OnBlur { get; init; }
}

public sealed class FocusState
{
    private readonly Dictionary<string, FocusSurfaceRegistration> _registry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FocusEntry> _history = new(StringComparer.Ordinal);
    private readonly List<FocusEntry> _stack = [];

    public FocusState(
        string windowId,
        IReadOnlyDictionary<string, Func<object?, bool>>? windowCommands = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        WindowId = windowId;
        WindowSurfaceId = $"window.{windowId}";

        var root = new FocusSurfaceRegistration
        {
            Id = WindowSurfaceId,
            Level = FocusLevel.Window,
            ParentId = null,
            Commands = windowCommands ?? new Dictionary<string, Func<object?, bool>>(StringComparer.Ordinal)
        };
        _registry.Add(root.Id, root);
        _stack.Add(new FocusEntry(root.Id, root.Level, null));
    }

    public string WindowId { get; }

    public string WindowSurfaceId { get; }

    public IReadOnlyList<FocusEntry> Stack => _stack.AsReadOnly();

    public IReadOnlyCollection<string> RegisteredSurfaceIds => _registry.Keys;

    public FocusEntry ActiveEntry => _stack[^1];

    public bool HasActiveModal => FindModalIndex() >= 0;

    public void Register(FocusSurfaceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.ParentId);

        if (registration.Level is FocusLevel.App or FocusLevel.Window)
        {
            throw new ArgumentException("Only a surface or object can be registered inside a window.", nameof(registration));
        }

        if (_registry.ContainsKey(registration.Id))
        {
            throw new InvalidOperationException($"Focus surface '{registration.Id}' is already registered.");
        }

        if (!_registry.ContainsKey(registration.ParentId))
        {
            throw new InvalidOperationException($"Parent focus surface '{registration.ParentId}' is not registered.");
        }

        _registry.Add(registration.Id, registration);
    }

    public bool PushFocus(string surfaceId, string? triggerId = null)
    {
        var registration = GetRegistration(surfaceId);
        if (!IsOnStack(registration.ParentId!))
        {
            throw new InvalidOperationException($"Parent focus surface '{registration.ParentId}' is not active.");
        }

        var modalIndex = FindModalIndex();
        if (modalIndex >= 0 && _stack.FindIndex(entry =>
                string.Equals(entry.SurfaceId, registration.ParentId, StringComparison.Ordinal)) < modalIndex)
        {
            throw new InvalidOperationException("A modal surface blocks focus changes behind it.");
        }

        var existingIndex = _stack.FindIndex(entry => string.Equals(entry.SurfaceId, surfaceId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            while (_stack.Count - 1 > existingIndex)
            {
                DeactivateTop();
            }

            ActivateTop();
            return false;
        }

        if (string.Equals(ActiveEntry.SurfaceId, surfaceId, StringComparison.Ordinal))
        {
            return false;
        }

        BlurActive();
        var entry = CreateEntry(registration, triggerId);
        _stack.Add(entry);
        registration.OnFocus?.Invoke(entry);
        return true;
    }

    public bool SwitchFocus(string surfaceId)
    {
        var registration = GetRegistration(surfaceId);
        if (registration.Modal)
        {
            throw new InvalidOperationException("A modal surface must be pushed onto the focus stack.");
        }

        if (!IsOnStack(registration.ParentId!))
        {
            throw new InvalidOperationException($"Parent focus surface '{registration.ParentId}' is not active.");
        }

        var modalIndex = FindModalIndex();
        var current = ActiveEntry;
        if (string.Equals(current.SurfaceId, surfaceId, StringComparison.Ordinal))
        {
            return false;
        }

        var existingIndex = _stack.FindIndex(entry => string.Equals(entry.SurfaceId, surfaceId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            if (modalIndex >= 0 && existingIndex < modalIndex)
            {
                throw new InvalidOperationException("A modal surface blocks focus changes behind it.");
            }

            while (_stack.Count - 1 > existingIndex)
            {
                DeactivateTop();
            }

            ActivateTop();
            return true;
        }

        if (string.Equals(current.SurfaceId, WindowSurfaceId, StringComparison.Ordinal))
        {
            return PushFocus(surfaceId);
        }

        if (modalIndex >= 0 && _stack.FindIndex(entry =>
                string.Equals(entry.SurfaceId, registration.ParentId, StringComparison.Ordinal)) < modalIndex)
        {
            throw new InvalidOperationException("A modal surface blocks focus changes behind it.");
        }

        var currentRegistration = GetRegistration(current.SurfaceId);
        if (!string.Equals(currentRegistration.ParentId, registration.ParentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Focus can switch directly only between sibling surfaces.");
        }

        DeactivateTop();
        var next = CreateEntry(registration, null);
        _stack.Add(next);
        registration.OnFocus?.Invoke(next);
        return true;
    }

    public bool PopFocus(string surfaceId)
    {
        if (string.Equals(surfaceId, WindowSurfaceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The window surface cannot be popped.");
        }

        var index = _stack.FindIndex(entry => string.Equals(entry.SurfaceId, surfaceId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        var modalIndex = FindModalIndex();
        if (modalIndex >= 0 && index < modalIndex &&
            !IsAncestorOf(surfaceId, _stack[modalIndex].SurfaceId))
        {
            throw new InvalidOperationException("A modal surface blocks closing its parent surfaces.");
        }

        while (_stack.Count > index)
        {
            DeactivateTop();
        }

        ActivateTop();
        return true;
    }

    public void Unregister(string surfaceId)
    {
        if (string.Equals(surfaceId, WindowSurfaceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The window surface cannot be unregistered.");
        }

        var children = _registry.Values
            .Where(registration => string.Equals(registration.ParentId, surfaceId, StringComparison.Ordinal))
            .Select(registration => registration.Id)
            .ToArray();
        foreach (var childId in children)
        {
            Unregister(childId);
        }

        PopFocus(surfaceId);
        _registry.Remove(surfaceId);
        _history.Remove(surfaceId);
    }

    public void RecordActivePosition(
        string? controlId = null,
        int? caretStart = null,
        int? caretEnd = null,
        double? scrollX = null,
        double? scrollY = null,
        object? selection = null)
    {
        ActiveEntry.RecordPosition(controlId, caretStart, caretEnd, scrollX, scrollY, selection);
    }

    public bool Dispatch(string commandId, object? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        for (var index = _stack.Count - 1; index >= 0; index--)
        {
            var registration = GetRegistration(_stack[index].SurfaceId);
            if (registration.Commands.TryGetValue(commandId, out var handler) && handler(arguments))
            {
                return true;
            }

            if (registration.Modal)
            {
                return false;
            }
        }

        return false;
    }

    private FocusSurfaceRegistration GetRegistration(string surfaceId) =>
        _registry.TryGetValue(surfaceId, out var registration)
            ? registration
            : throw new KeyNotFoundException($"Focus surface '{surfaceId}' is not registered.");

    private bool IsOnStack(string surfaceId) =>
        _stack.Any(entry => string.Equals(entry.SurfaceId, surfaceId, StringComparison.Ordinal));

    private bool IsAncestorOf(string ancestorId, string surfaceId)
    {
        var parentId = GetRegistration(surfaceId).ParentId;
        while (parentId is not null)
        {
            if (string.Equals(parentId, ancestorId, StringComparison.Ordinal))
            {
                return true;
            }

            parentId = GetRegistration(parentId).ParentId;
        }

        return false;
    }

    private int FindModalIndex()
    {
        for (var index = _stack.Count - 1; index > 0; index--)
        {
            if (GetRegistration(_stack[index].SurfaceId).Modal)
            {
                return index;
            }
        }

        return -1;
    }

    private FocusEntry CreateEntry(FocusSurfaceRegistration registration, string? triggerId)
    {
        if (_history.TryGetValue(registration.Id, out var previous))
        {
            return previous.CopyForReactivation(triggerId);
        }

        var controlId = registration.InitialFocus?.Invoke();
        var entry = new FocusEntry(registration.Id, registration.Level, triggerId);
        if (controlId is not null)
        {
            entry.RecordPosition(controlId, null, null, null, null, null);
        }

        return entry;
    }

    private void BlurActive()
    {
        var entry = ActiveEntry;
        var registration = GetRegistration(entry.SurfaceId);
        registration.OnBlur?.Invoke(entry);
        _history[entry.SurfaceId] = entry.CopyForReactivation(entry.TriggerId);
    }

    private void DeactivateTop()
    {
        BlurActive();
        _stack.RemoveAt(_stack.Count - 1);
    }

    private void ActivateTop()
    {
        var entry = ActiveEntry;
        GetRegistration(entry.SurfaceId).OnFocus?.Invoke(entry);
    }
}
