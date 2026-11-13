using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DuoWall.Core.Widgets;

namespace DuoWall.App.Windows;

public partial class CalendarEntryDialogWindow : Window
{
    private readonly DateTime _selectedDate;
    private readonly List<CalendarAgendaEntry> _entries;
    private readonly Action<CalendarAgendaEntry> _onSave;
    private readonly Action<string> _onDelete;

    public CalendarEntryDialogWindow(
        DateTime selectedDate,
        IEnumerable<CalendarAgendaEntry> entriesForDate,
        Action<CalendarAgendaEntry> onSave,
        Action<string> onDelete)
    {
        InitializeComponent();
        _selectedDate = selectedDate.Date;
        _entries = entriesForDate.ToList();
        _onSave = onSave;
        _onDelete = onDelete;

        DateHeaderText.Text = _selectedDate.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        string? holiday = CalendarHolidayHelper.GetHolidayName(_selectedDate);
        if (!string.IsNullOrWhiteSpace(holiday))
        {
            HolidaySubText.Text = $"Public Holiday: {holiday}";
            HolidaySubText.Visibility = Visibility.Visible;
        }

        // Default start time rounded nicely to next half-hour if adding for today
        if (_selectedDate == DateTime.Today)
        {
            var next = DateTime.Now.AddMinutes(30);
            TimeBox.Text = $"{next.Hour:00}:00";
        }

        RenderExistingEntries();
        TitleBox.Focus();
    }

    private void KindRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (TimeSettingsPanel == null) return;
        TimeSettingsPanel.Visibility = KindNoteRadio.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderExistingEntries()
    {
        ExistingEntriesHost.Children.Clear();
        if (_entries.Count == 0)
        {
            ExistingEntriesHost.Children.Add(new TextBlock
            {
                Text = "Nothing scheduled on this date yet.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 2, 0, 2)
            });
            return;
        }

        foreach (var entry in _entries)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var dock = new DockPanel();
            var del = new Button
            {
                Content = "Delete",
                Background = new SolidColorBrush(Color.FromRgb(127, 29, 29)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            del.Click += (_, _) =>
            {
                _entries.Remove(entry);
                _onDelete(entry.Id);
                RenderExistingEntries();
            };
            DockPanel.SetDock(del, Dock.Right);
            dock.Children.Add(del);

            string timePart = entry.Kind == CalendarAgendaEntryKind.Note || string.IsNullOrWhiteSpace(entry.StartTimeText)
                ? entry.Kind.ToString()
                : $"{entry.StartTimeText} ({entry.DurationMinutes}m) • {entry.Kind}";
            string alertPart = entry.Kind == CalendarAgendaEntryKind.Note
                ? string.Empty
                : (entry.PlayAlarmSound ? " • Sound Alert" : " • Silent");

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = $"{entry.Title}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            textStack.Children.Add(new TextBlock
            {
                Text = timePart + alertPart,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            });
            dock.Children.Add(textStack);
            card.Child = dock;
            ExistingEntriesHost.Children.Add(card);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ValidationText.Text = "Please enter a title.";
            return;
        }

        var kind = KindReminderRadio.IsChecked == true
            ? CalendarAgendaEntryKind.Reminder
            : KindNoteRadio.IsChecked == true
                ? CalendarAgendaEntryKind.Note
                : CalendarAgendaEntryKind.Event;

        string timeText = string.Empty;
        int duration = 30;
        bool playSound = false;

        if (kind != CalendarAgendaEntryKind.Note)
        {
            if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var parsed))
            {
                ValidationText.Text = "Time must be HH:mm (e.g. 14:30).";
                return;
            }
            timeText = $"{parsed.Hours:00}:{parsed.Minutes:00}";
            if (!int.TryParse(DurationBox.Text.Trim(), out duration) || duration < 1 || duration > 1440)
                duration = 30;
            playSound = SoundAlertRadio.IsChecked == true;
        }

        var entry = new CalendarAgendaEntry
        {
            DateIso = _selectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Kind = kind,
            Title = title,
            Notes = NotesBox.Text.Trim(),
            StartTimeText = timeText,
            DurationMinutes = duration,
            PlayAlarmSound = playSound
        };

        _onSave(entry);
        _entries.Add(entry);
        TitleBox.Text = string.Empty;
        NotesBox.Text = string.Empty;
        ValidationText.Text = string.Empty;
        RenderExistingEntries();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

