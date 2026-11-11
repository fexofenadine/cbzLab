using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Tests;

public class UndoHistoryTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    private MainViewModel NewViewModel() =>
        new(_cfg.Schema, _cfg.Settings, _cfg.Validation, new RecentValuesService(_cfg.Settings, _cfg.Log));

    private static ComicFileViewModel Book(string name, params (string Tag, string Value)[] values) =>
        new("C:/x/" + name, ArchiveFormat.Cbz, null,
            values.ToDictionary(v => v.Tag, v => v.Value, StringComparer.Ordinal), 1);

    private static FieldViewModel Field(MainViewModel vm, string tag) => vm.AllFields.First(f => f.Tag == tag);

    [Fact]
    public void TypingInOneFieldIsOneStepUntilTheFieldIsLeft()
    {
        var vm = NewViewModel();
        var book = Book("a.cbz", ("Series", "Saga"));
        vm.AddFiles(new[] { book });
        vm.SetSelection(new[] { book });

        var series = Field(vm, "Series");
        series.Value = "Sag";
        series.Value = "Sa";
        series.Value = "Sandman";
        vm.History.Seal();
        series.Value = "Sandman 2";

        vm.Undo();
        Assert.Equal("Sandman", book.GetValue("Series"));
        Assert.Equal("Sandman", series.Value);
        vm.Undo();
        Assert.Equal("Saga", book.GetValue("Series"));
        Assert.False(book.IsDirty);
        Assert.False(vm.History.CanUndo);

        vm.Redo();
        vm.Redo();
        Assert.Equal("Sandman 2", book.GetValue("Series"));
        Assert.False(vm.History.CanRedo);
    }

    [Fact]
    public void ABatchEditUndoesOnEveryBookBackToItsOwnValue()
    {
        var vm = NewViewModel();
        var books = new[] { Book("a.cbz", ("Publisher", "Image")), Book("b.cbz", ("Publisher", "DC")), Book("c.cbz") };
        vm.AddFiles(books);
        vm.SetSelection(books);

        Field(vm, "Publisher").Value = "Vertigo";
        Assert.All(books, b => Assert.Equal("Vertigo", b.GetValue("Publisher")));

        vm.Undo();
        Assert.Equal(new[] { "Image", "DC", "" }, books.Select(b => b.GetValue("Publisher")));
        Assert.All(books, b => Assert.False(b.IsDirty));
        Assert.StartsWith("Undid: Edit ", vm.StatusText);
    }

    //the date box writes Year, Month and Day; one undo puts all three back
    [Fact]
    public void TheDateBoxIsOneStepAcrossItsThreeFields()
    {
        var vm = NewViewModel();
        var book = Book("a.cbz", ("Year", "2012"));
        vm.AddFiles(new[] { book });
        vm.SetSelection(new[] { book });

        var before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-GB");
        try
        {
            Field(vm, "Year").DateDisplayValue = "14/03/2015";
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
        Assert.Equal(("2015", "3", "14"), (book.GetValue("Year"), book.GetValue("Month"), book.GetValue("Day")));

        vm.Undo();
        Assert.Equal(("2012", "", ""), (book.GetValue("Year"), book.GetValue("Month"), book.GetValue("Day")));
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void AToolRunIsOneStepWhateverItTouches()
    {
        var vm = NewViewModel();
        var books = new[] { Book("a.cbz", ("Series", "Saga")), Book("b.cbz", ("Series", "Saga"), ("Title", "One")) };
        vm.AddFiles(books);

        using (vm.History.Begin("Find & Replace"))
        {
            books[0].SetValue("Series", "SAGA");
            books[1].SetValue("Series", "SAGA");
            books[1].ReplaceCurrentValues(new Dictionary<string, string> { ["Series"] = "SAGA", ["Writer"] = "BKV" });
        }
        Assert.Equal("Find & Replace", vm.History.UndoLabel);

        vm.Undo();
        Assert.Equal("Saga", books[0].GetValue("Series"));
        Assert.Equal(("Saga", "One", ""), (books[1].GetValue("Series"), books[1].GetValue("Title"), books[1].GetValue("Writer")));
        Assert.Equal("Find & Replace", vm.History.RedoLabel);

        vm.Redo();
        Assert.Equal(("SAGA", "", "BKV"), (books[1].GetValue("Series"), books[1].GetValue("Title"), books[1].GetValue("Writer")));
    }

    [Fact]
    public void ANewEditClearsRedoAndAnEmptyStepIsNotKept()
    {
        var vm = NewViewModel();
        var book = Book("a.cbz", ("Series", "Saga"));
        vm.AddFiles(new[] { book });

        using (vm.History.Begin("Nothing"))
            book.SetValue("Series", "Saga");
        Assert.False(vm.History.CanUndo);

        using (vm.History.Begin("One"))
            book.SetValue("Series", "A");
        vm.Undo();
        Assert.True(vm.History.CanRedo);
        using (vm.History.Begin("Two"))
            book.SetValue("Series", "B");
        Assert.False(vm.History.CanRedo);
    }

    //a closed book, or one reloaded from disk, drops out of every step; steps left empty go too
    [Fact]
    public void ClosedAndRevertedBooksAreForgotten()
    {
        var vm = NewViewModel();
        var a = Book("a.cbz");
        var b = Book("b.cbz");
        vm.AddFiles(new[] { a, b });

        using (vm.History.Begin("Both"))
        {
            a.SetValue("Series", "A");
            b.SetValue("Series", "B");
        }
        using (vm.History.Begin("Only a"))
            a.SetValue("Title", "T");

        vm.RemoveFiles(new[] { a });
        Assert.Equal("Both", vm.History.UndoLabel);
        a.SetValue("Series", "changed after closing");

        vm.Undo();
        Assert.Equal("", b.GetValue("Series"));
        Assert.Equal("changed after closing", a.GetValue("Series"));
        Assert.False(vm.History.CanUndo);

        vm.Redo();
        b.ReloadFrom(null, new Dictionary<string, string>(), 1);
        Assert.False(vm.History.CanUndo);
        Assert.False(vm.History.CanRedo);
    }

    [Fact]
    public void SeededValuesAreNotEdits()
    {
        var vm = NewViewModel();
        var book = Book("a.cbz");
        vm.AddFiles(new[] { book });
        book.SeedValue("PageCount", "24");
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void KeepsOnlyTheNewestSteps()
    {
        var vm = NewViewModel();
        var book = Book("a.cbz");
        vm.AddFiles(new[] { book });
        for (var i = 1; i <= UndoHistory.MaxSteps + 5; i++)
            using (vm.History.Begin("step " + i))
                book.SetValue("Series", i.ToString());

        var undone = 0;
        while (vm.History.CanUndo)
        {
            vm.Undo();
            undone++;
        }
        Assert.Equal(UndoHistory.MaxSteps, undone);
        Assert.Equal("5", book.GetValue("Series"));
    }

    //grid view's cells listen for this to read their value again
    [Fact]
    public void EveryChangeToABooksValuesIsAnnounced()
    {
        var book = Book("a.cbz", ("Series", "Saga"));
        var announced = 0;
        book.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ComicFileViewModel.EditVersion))
                announced++;
        };
        book.SetValue("Series", "A");
        book.RevertField("Series");
        book.ReplaceCurrentValues(new Dictionary<string, string> { ["Title"] = "T" });
        book.ReloadFrom(null, new Dictionary<string, string>(), 1);
        Assert.Equal(4, announced);
    }
}

public class FindReplaceTests
{
    private static ComicFileViewModel Book(string name, params (string Tag, string Value)[] values) =>
        new("C:/x/" + name, ArchiveFormat.Cbz, null,
            values.ToDictionary(v => v.Tag, v => v.Value, StringComparer.Ordinal), 1);

    private static readonly ComicFileViewModel[] Books =
    {
        Book("a.cbz", ("Series", "The Sandman"), ("Writer", "Neil Gaiman"), ("Summary", "the sandman returns")),
        Book("b.cbz", ("Series", "Saga"), ("Writer", "Brian K. Vaughan")),
        Book("c.cbz"),
    };

    [Fact]
    public void PlainTextInOneFieldIgnoresCaseByDefault()
    {
        var edits = FindReplace.Plan(Books, "Series", new FindReplace.Options("the ", "", false, false));
        var edit = Assert.Single(edits);
        Assert.Equal((Books[0], "Series", "Sandman"), (edit.File, edit.Tag, edit.NewValue));
        Assert.Empty(FindReplace.Plan(Books, "Series", new FindReplace.Options("the ", "", true, false)));
    }

    [Fact]
    public void AllFieldsReachesEveryFieldWithAValue()
    {
        var edits = FindReplace.Plan(Books, null, new FindReplace.Options("sandman", "Dreaming", false, false));
        Assert.Equal(new[] { "Series", "Summary" }, edits.Select(e => e.Tag).OrderBy(t => t));
        Assert.All(edits, e => Assert.Same(Books[0], e.File));
    }

    [Fact]
    public void RegexCanReorderWithGroups()
    {
        var edits = FindReplace.Plan(Books, "Writer", new FindReplace.Options(@"^(\S+) (.+)$", "$2, $1", true, true));
        Assert.Equal(new[] { "Gaiman, Neil", "K. Vaughan, Brian" }, edits.Select(e => e.NewValue));
    }

    //a regex special character typed as plain text is just a character
    [Fact]
    public void PlainTextIsNeverReadAsAPattern()
    {
        var book = Book("d.cbz", ("Title", "What If...? (2021)"));
        var edit = Assert.Single(FindReplace.Plan(new[] { book }, "Title", new FindReplace.Options("...?", "", false, false)));
        Assert.Equal("What If (2021)", edit.NewValue);
    }

    [Fact]
    public void ABadPatternIsExplainedAndAnEmptyFindDoesNothing()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            FindReplace.Plan(Books, null, new FindReplace.Options("(unclosed", "", false, true)));
        Assert.StartsWith("That isn't a valid regular expression", ex.Message);
        Assert.Empty(FindReplace.Plan(Books, null, new FindReplace.Options("", "x", false, true)));
    }

    [Fact]
    public void NothingIsChangedByPlanning()
    {
        FindReplace.Plan(Books, null, new FindReplace.Options("a", "b", false, false));
        Assert.Equal("The Sandman", Books[0].GetValue("Series"));
        Assert.All(Books, b => Assert.False(b.IsDirty));
    }
}

public class FileNameTemplateTests
{
    private static Func<string, string> Values(params (string Tag, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Tag, v => v.Value);
        return tag => map.TryGetValue(tag, out var v) ? v : "";
    }

    [Theory]
    [InlineData("{Series} #{Number:000} ({Year})", "Saga #007 (2012)")]
    [InlineData("{Series} #{Number}", "Saga #7")]
    [InlineData("{Series} v{Volume} #{Number:00}", "Saga v2 #07")]
    [InlineData("{Year} - {Series} - {Title}", "2012 - Saga - Chapter Seven")]
    [InlineData("[{Publisher}] {Series}", "[Image] Saga")]
    public void FillsInFieldsAndPadsNumbers(string template, string expected) =>
        Assert.Equal(expected, FileNameTemplate.Format(template, Values(
            ("Series", "Saga"), ("Number", "7"), ("Volume", "2"), ("Year", "2012"), ("Title", "Chapter Seven"), ("Publisher", "Image"))));

    //an empty field takes its "#", brackets and separators with it
    [Theory]
    [InlineData("{Series} #{Number:000} ({Year})", "Saga")]
    [InlineData("{Series} - {Title} - {Year}", "Saga")]
    [InlineData("[{Publisher}] {Series} ({Year})", "Saga")]
    [InlineData("{Title} - {Series}", "Saga")]
    [InlineData("{Year} {Number}", "")]
    [InlineData("no fields at all", "")]
    public void EmptyFieldsLeaveNothingBehind(string template, string expected) =>
        Assert.Equal(expected, FileNameTemplate.Format(template, Values(("Series", "Saga"))));

    [Theory]
    [InlineData("7", "007")]
    [InlineData("7.5", "007.5")]
    [InlineData("12a", "012a")]
    [InlineData("1234", "1234")]
    [InlineData("Annual 1", "Annual 1")]
    public void PaddingOnlyTouchesALeadingNumber(string number, string expected) =>
        Assert.Equal(expected, FileNameTemplate.Format("{Number:000}", Values(("Number", number))));

    [Theory]
    [InlineData("Batman: Year One", "Batman - Year One")]
    [InlineData("What If...?", "What If")]
    [InlineData("AC/DC \"Live\"", "AC-DC 'Live'")]
    [InlineData("  spaced   out \n title ", "spaced out title")]
    [InlineData("<*>", "")]
    [InlineData("NUL", "_NUL")]
    public void NamesAreSafeOnEveryDesktop(string series, string expected) =>
        Assert.Equal(expected, FileNameTemplate.Format("{Series}", Values(("Series", series))));

    [Fact]
    public void VeryLongNamesAreCut()
    {
        var name = FileNameTemplate.Format("{Summary}", Values(("Summary", new string('a', 500))));
        Assert.Equal(180, name.Length);
    }

    private static string P(string name) => Path.Combine(Path.GetTempPath(), "cbzLabTests_rename", name);

    [Fact]
    public void PlansRenamesWithoutEverOverwriting()
    {
        var books = new List<(string, Func<string, string>)>
        {
            (P("a.cbz"), Values(("Series", "Saga"), ("Number", "1"))),
            (P("b.cbr"), Values(("Series", "Saga"), ("Number", "1"))),
            (P("c.cbz"), Values(("Series", "Saga"), ("Number", "2"))),
            (P("Saga #003.cbz"), Values(("Series", "Saga"), ("Number", "3"))),
            (P("e.cbz"), Values()),
            (P("f.cbz"), Values(("Series", "Saga"), ("Number", "3"))),
            (P("g.cbz"), Values(("Series", "Saga"), ("Number", "1"))),
        };
        var onDisk = new HashSet<string>(books.Select(b => b.Item1)) { P("Saga #002.cbz") };

        var plan = FileNameTemplate.Plan(books, "{Series} #{Number:000}", onDisk.Contains);

        Assert.Equal(new[]
        {
            RenameStatus.Ready,      //a.cbz -> Saga #001.cbz
            RenameStatus.Ready,      //b.cbr -> Saga #001.cbr: its own extension, so no clash
            RenameStatus.Blocked,    //Saga #002.cbz is on disk already
            RenameStatus.Unchanged,
            RenameStatus.Blocked,    //nothing to name it from
            RenameStatus.Blocked,    //the name the unchanged book already has
            RenameStatus.Blocked,    //the name a.cbz is taking
        }, plan.Select(i => i.Status));
        Assert.Equal("Saga #001.cbz", plan[0].NewName);
        Assert.Equal(P("Saga #001.cbz"), plan[0].NewPath);
        Assert.Equal("Saga #001.cbr", plan[1].NewName);
        Assert.All(plan.Where(i => i.Status == RenameStatus.Blocked), i => Assert.NotEqual("", i.Problem));
    }

    //windows and macos treat a change of case as the same file, which is a rename, not a clash
    [Fact]
    public void AChangeOfCaseAloneIsAllowedWhereTheFileSystemFoldsCase()
    {
        if (OperatingSystem.IsLinux())
            return;
        var books = new List<(string, Func<string, string>)> { (P("saga.cbz"), Values(("Series", "Saga"))) };
        var plan = FileNameTemplate.Plan(books, "{Series}", path => PathComparison.Same(path, P("saga.cbz")));
        Assert.Equal(RenameStatus.Ready, plan[0].Status);
        Assert.Equal("Saga.cbz", plan[0].NewName);
    }
}

public class RecentFilesLimitTests : IDisposable
{
    private readonly TestConfig _cfg = new();
    public void Dispose() => _cfg.Dispose();

    [Fact]
    public void LoweringTheLimitShortensTheListStraightAway()
    {
        _cfg.Settings.Settings.MaxRecentFiles = 8;
        _cfg.Settings.AddRecentFiles(Enumerable.Range(1, 8).Select(i => $"C:/x/{i}.cbz"));
        _cfg.Settings.Settings.MaxRecentFiles = 3;
        _cfg.Settings.TrimRecentFiles();
        Assert.Equal(new[] { "C:/x/8.cbz", "C:/x/7.cbz", "C:/x/6.cbz" }, _cfg.Settings.Settings.RecentFiles);
    }

    [Fact]
    public void NewSettingsSurviveARoundTrip()
    {
        _cfg.Settings.Settings.WindowMaximised = true;
        _cfg.Settings.Settings.RenameTemplate = "{Series} v{Volume}";
        _cfg.Settings.Save();
        _cfg.Settings.Load();
        Assert.True(_cfg.Settings.Settings.WindowMaximised);
        Assert.Equal("{Series} v{Volume}", _cfg.Settings.Settings.RenameTemplate);
        Assert.Contains("\"window_maximised\": true", File.ReadAllText(_cfg.Settings.SettingsPath));
    }
}
