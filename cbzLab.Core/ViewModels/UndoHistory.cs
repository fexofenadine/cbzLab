namespace cbzLab.ViewModels;

/// <summary>
/// Undo and redo for edits to field values, across every open book. A step is everything one
/// action changed (a tool run over a thousand books is one step); typing in a field grows a single
/// step until the field is left, rather than one per keystroke. Saving doesn't clear it: undoing
/// past a save just leaves the book unsaved again.
/// </summary>
public sealed class UndoHistory
{
    //plenty for a sitting, and a bound on the memory a long one holds
    public const int MaxSteps = 100;

    private sealed class Step
    {
        public required string Label { get; init; }
        public string? MergeKey { get; init; }
        public bool Sealed { get; set; }

        //first value before and last value after, per book and field, in the order first touched
        public Dictionary<(ComicFileViewModel File, string Tag), (string Before, string After)> Changes { get; } = new();

        public void Add(ComicFileViewModel file, string tag, string before, string after)
        {
            var key = (file, tag);
            Changes[key] = Changes.TryGetValue(key, out var seen) ? (seen.Before, after) : (before, after);
        }

        public bool SameBooksAs(Step other) =>
            Changes.Keys.Select(k => k.File).ToHashSet().SetEquals(other.Changes.Keys.Select(k => k.File));
    }

    private readonly List<Step> _undo = new();
    private readonly List<Step> _redo = new();
    private Step? _open;
    private int _depth;
    private bool _applying;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>
    /// Everything changed until the result is disposed becomes one step. Steps opened with the same
    /// non-null <paramref name="mergeKey"/>, touching the same books, join the one before (typing)
    /// until <see cref="Seal"/>. A scope opened inside another just joins it.
    /// </summary>
    public IDisposable Begin(string label, string? mergeKey = null)
    {
        if (_depth++ == 0)
            _open = new Step { Label = label, MergeKey = mergeKey };
        return new Scope(this);
    }

    //wired to every open book's ValueChanged
    public void Record(ComicFileViewModel file, string tag, string before, string after)
    {
        if (_applying || before == after)
            return;
        if (_open is not null)
        {
            _open.Add(file, tag, before, after);
            return;
        }
        //a change made outside any scope still gets undone, as a step of its own
        var step = new Step { Label = tag };
        step.Add(file, tag, before, after);
        Push(step);
    }

    private void End()
    {
        if (--_depth > 0 || _open is not { } step)
            return;
        _open = null;
        foreach (var same in step.Changes.Where(c => c.Value.Before == c.Value.After).Select(c => c.Key).ToList())
            step.Changes.Remove(same);
        if (step.Changes.Count == 0)
            return;

        if (step.MergeKey is not null && _undo.Count > 0 && _undo[^1] is { Sealed: false } last
            && last.MergeKey == step.MergeKey && last.SameBooksAs(step))
        {
            foreach (var (key, change) in step.Changes)
                last.Add(key.File, key.Tag, change.Before, change.After);
            _redo.Clear();
            return;
        }
        Push(step);
    }

    private void Push(Step step)
    {
        _undo.Add(step);
        if (_undo.Count > MaxSteps)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    //the next edit starts a step of its own, even in the same field
    public void Seal()
    {
        if (_undo.Count > 0)
            _undo[^1].Sealed = true;
    }

    /// <summary>Puts back what the last step changed. Returns its label, or null with nothing to undo.</summary>
    public string? Undo() => Move(_undo, _redo, c => c.Before);

    public string? Redo() => Move(_redo, _undo, c => c.After);

    private string? Move(List<Step> from, List<Step> to, Func<(string Before, string After), string> value)
    {
        if (from.Count == 0 || _open is not null)
            return null;
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        step.Sealed = true;

        _applying = true;
        try
        {
            foreach (var (key, change) in step.Changes)
                key.File.SetValue(key.Tag, value(change));
        }
        finally
        {
            _applying = false;
        }
        to.Add(step);
        return step.Label;
    }

    //books that were closed or reloaded from disk: their edits can't be undone any more
    public void Forget(IEnumerable<ComicFileViewModel> files)
    {
        var gone = files.ToHashSet();
        if (gone.Count == 0)
            return;
        foreach (var stack in new[] { _undo, _redo })
        {
            foreach (var step in stack)
                foreach (var key in step.Changes.Keys.Where(k => gone.Contains(k.File)).ToList())
                    step.Changes.Remove(key);
            stack.RemoveAll(s => s.Changes.Count == 0);
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private sealed class Scope : IDisposable
    {
        private UndoHistory? _owner;
        public Scope(UndoHistory owner) => _owner = owner;

        public void Dispose()
        {
            _owner?.End();
            _owner = null;
        }
    }
}
